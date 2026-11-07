using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed class OtpService
{
    public const string Registration = "registration";
    public const string PasswordReset = "password-reset";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object _gate = new();
    private readonly ISmsSender _smsSender;
    private readonly ILogger<OtpService> _logger;
    private readonly TimeProvider _time;

    public OtpService(ISmsSender smsSender, ILogger<OtpService> logger, TimeProvider time)
    {
        _smsSender = smsSender;
        _logger = logger;
        _time = time;
    }

    public async Task<(bool IsDevelopmentOtp, string? DevelopmentOtp)> SendOtpAsync(
        string phoneNumber, bool isDevelopment, CancellationToken cancellationToken = default,
        string purpose = Registration, string? registrationName = null)
    {
        var phone = MobileNumber.Normalize(phoneNumber);
        if (purpose != Registration && purpose != PasswordReset) throw new ArgumentException("Unsupported OTP purpose.", nameof(purpose));
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"otp:{purpose}:{phone}";
        lock (_gate)
        {
            if (_cache.TryGetValue<DateTimeOffset>($"cooldown:{phone}", out var until) && until > _time.GetUtcNow()) throw new OtpRateLimitException();
            Set($"cooldown:{phone}", _time.GetUtcNow().AddMinutes(1), TimeSpan.FromMinutes(1));
            _cache.Remove(key);
        }
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var sent = false;
        try { sent = await _smsSender.SendOtpAsync(phone, otp, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("SMS OTP delivery failed ({ExceptionType}).", ex.GetType().Name);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Keep response compatibility, but never substitute or disclose a backend OTP.
        // Tests inject a sender fake; the client demo is entirely frontend-only.
        if (!sent) throw new OtpDeliveryException();
        lock (_gate) Set(key, new OtpEntry(Hash(phone, otp), _time.GetUtcNow().Add(Lifetime), registrationName?.Trim()), Lifetime);
        return (false, null);
    }

    public string? VerifyOtp(string phoneNumber, string otp, string purpose = Registration)
    {
        if (!MobileNumber.TryNormalize(phoneNumber, out var phone) || string.IsNullOrWhiteSpace(otp) ||
            (purpose != Registration && purpose != PasswordReset)) return null;
        var key = $"otp:{purpose}:{phone}";
        lock (_gate)
        {
            if (!_cache.TryGetValue<OtpEntry>(key, out var entry) || entry is null) return null;
            if (entry.ExpiresAt <= _time.GetUtcNow() || ++entry.Attempts > 5)
            {
                _cache.Remove(key);
                return null;
            }
            if (!CryptographicOperations.FixedTimeEquals(entry.Hash, Hash(phone, otp.Trim()))) return null;
            _cache.Remove(key);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Set($"token:{token}", new VerifiedPhone(phone, purpose, _time.GetUtcNow().Add(Lifetime), entry.Name), Lifetime);
            return token;
        }
    }

    public bool ConsumeVerificationToken(string? token, string phoneNumber, string purpose = Registration, string? registrationName = null)
    {
        if (string.IsNullOrEmpty(token) || !MobileNumber.TryNormalize(phoneNumber, out var phone)) return false;
        lock (_gate)
        {
            var key = $"token:{token}";
            if (!_cache.TryGetValue<VerifiedPhone>(key, out var entry) || entry is null ||
                entry.Phone != phone || entry.Purpose != purpose || entry.ExpiresAt <= _time.GetUtcNow() ||
                (entry.Name is not null && !string.Equals(entry.Name, registrationName?.Trim(), StringComparison.Ordinal))) return false;
            _cache.Remove(key);
            return true;
        }
    }

    private void Set<T>(string key, T value, TimeSpan lifetime) =>
        _cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = lifetime });
    private static byte[] Hash(string phone, string otp) => SHA256.HashData(Encoding.UTF8.GetBytes($"{phone}:{otp}"));
    private sealed record VerifiedPhone(string Phone, string Purpose, DateTimeOffset ExpiresAt, string? Name);
    private sealed class OtpEntry(byte[] hash, DateTimeOffset expiresAt, string? name)
    {
        public string? Name { get; } = name;
        public byte[] Hash { get; } = hash;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public int Attempts { get; set; }
    }
}

public sealed class OtpRateLimitException : Exception { }
public sealed class OtpDeliveryException : Exception { }
