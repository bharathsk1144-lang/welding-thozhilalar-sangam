using Microsoft.Extensions.Options;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

// Replace only after official provider documentation and credentials are reviewed.
// Configuration alone cannot enable a guessed HTTP contract.
public sealed class ConfiguredSmsSender : ISmsSender
{
    private readonly SmsSettings _settings;
    private readonly ILogger<ConfiguredSmsSender> _logger;
    public ConfiguredSmsSender(HttpClient httpClient, IOptions<SmsSettings> settings, ILogger<ConfiguredSmsSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }
    public Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_settings.Enabled)
            _logger.LogWarning("OTP delivery is unavailable: a reviewed provider adapter has not been configured.");
        return Task.FromResult(false);
    }
}
