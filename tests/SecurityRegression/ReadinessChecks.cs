using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;
using WeldingThozhilalarSangam.Api.Middleware;
using Microsoft.Extensions.Logging;

static class ReadinessChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using (var unusedClient = new HttpClient())
        {
            for (var i = 0; i < 50; i++)
            {
                using var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                reservation.Start(); var port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
                await using var bridge = new TestBridge(port, unusedClient, new CapturingSms(), "unused-synthetic-key");
            }
            check(true, "test-only loopback bridge survives 50 start/stop cycles without a shutdown race");
        }
        foreach (var development in new[] { false, true })
        {
            var unavailable = new OtpService(new RefusingSender(), NullLogger<OtpService>.Instance, TimeProvider.System);
            try { await unavailable.SendOtpAsync("9876543300", development); throw new Exception("Undelivered OTP accepted"); }
            catch (OtpDeliveryException) { }
            check(unavailable.VerifyOtp("9876543300", "123456") is null, $"backend has no fixed OTP fallback (Development={development})");
        }
        var capture = new CapturingSms();
        var clock = new Clock();
        var service = new OtpService(capture, NullLogger<OtpService>.Instance, clock);
        var sent = await service.SendOtpAsync("9876543301", false, registrationName: "Synthetic");
        check(!sent.IsDevelopmentOtp && sent.DevelopmentOtp is null && capture.Code("9876543301").Length == 6, "successful mock delivery never returns the random OTP to the client");
        check(service.VerifyOtp("9876543301", "000000") is null, "incorrect random OTP fails");
        var proof = service.VerifyOtp("9876543301", capture.Code("9876543301"));
        check(proof is not null && service.VerifyOtp("9876543301", capture.Code("9876543301")) is null, "verified OTP cannot be replayed");
        check(!service.ConsumeVerificationToken(proof, "9876543301", registrationName: "Tampered") &&
            service.ConsumeVerificationToken(proof, "9876543301", registrationName: "Synthetic") &&
            !service.ConsumeVerificationToken(proof, "9876543301", registrationName: "Synthetic"), "proof binds name and is consumed once");
        try { await service.SendOtpAsync("+919876543301", false); throw new Exception("Cooldown failed"); }
        catch (OtpRateLimitException) { check(true, "canonical resend cooldown enforced"); }
        clock.Advance(TimeSpan.FromSeconds(61));
        await service.SendOtpAsync("9876543301", false);
        check(service.VerifyOtp("9876543301", capture.Code("9876543301"), OtpService.PasswordReset) is null, "registration OTP cannot reset a password");
        clock.Advance(TimeSpan.FromMinutes(6));
        check(service.VerifyOtp("9876543301", capture.Code("9876543301")) is null, "unverified challenge expires");
        await service.SendOtpAsync("9876543301", false);
        check(service.VerifyOtp("9876543301", capture.Code("9876543301")) is not null, "resend succeeds after cooldown");
        var cancelled = new OtpService(new CancellingSender(), NullLogger<OtpService>.Instance, clock);
        try { await cancelled.SendOtpAsync("9876543302", false); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check(cancelled.VerifyOtp("9876543302", "123456") is null, "cancelled delivery creates no challenge"); }
        var transport = new RejectNetwork();
        var sender = new ConfiguredSmsSender(new HttpClient(transport), Options.Create(new SmsSettings {
            Enabled = true, Provider = "Unreviewed", ApiKey = "synthetic", SenderId = "synthetic", TemplateId = "synthetic"
        }), NullLogger<ConfiguredSmsSender>.Instance);
        check(!await sender.SendOtpAsync("9876543303", "654321") && transport.Calls == 0, "unimplemented provider fails closed without sending a network request");

        IFormFile File(string filename, string mime, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "sample", filename) { Headers = new HeaderDictionary(), ContentType = mime };
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII=");
        check(await UploadValidation.ValidateAsync(File("a.png", "image/png", png), 1024, "Photo") is null, "valid PNG signature accepted");
        check(await UploadValidation.ValidateAsync(File("a.png", "text/html", png), 1024, "Photo") is not null, "MIME mismatch rejected");
        check(await UploadValidation.ValidateAsync(File("a.png", "image/png", "<script>bad</script>"u8.ToArray()), 1024, "Photo") is not null, "renamed HTML rejected");
        check(await UploadValidation.ValidateAsync(File("a.svg", "image/svg+xml", png), 1024, "Photo") is not null, "active SVG upload rejected");
        check(await UploadValidation.ValidateAsync(File("a.png", "image/png", [137, 80]), 1024, "Photo") is not null, "truncated signature rejected");
        check(await UploadValidation.ValidateAsync(File("a.png", "image/png", png), 8, "Photo") is not null, "oversized upload rejected");
        check(await UploadValidation.ValidateAsync(null, 1024, "Photo") is not null && await UploadValidation.ValidateAsync(null, 1024, "Proof", required: false) is null, "required and optional uploads retain their behavior");
        check(await UploadValidation.ValidateAsync(File("a.pdf", "application/pdf", "%PDF-1.7\nsynthetic"u8.ToArray()), 1024, "Proof", allowPdf: true) is null &&
            await UploadValidation.ValidateAsync(File("a.pdf", "application/pdf", "%PDF-1.7\nsynthetic"u8.ToArray()), 1024, "Photo") is not null, "PDF permitted only for payment proofs");

        var admin = new AdminSettings { Username = "qa-admin", JwtKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), Issuer = "test", Audience = "test", Password = "ignored" };
        admin.PasswordHash = new PasswordHasher<AdminSettings>().HashPassword(admin, "SyntheticAdmin@123");
        var auth = new AdminAuthService(Options.Create(admin));
        check(auth.Login("qa-admin", "SyntheticAdmin@123") is not null && auth.Login("qa-admin", "wrong") is null && auth.Login("wrong", "SyntheticAdmin@123") is null, "admin hash authentication rejects wrong password/identity");
        admin.PasswordHash = "invalid";
        check(auth.Login("qa-admin", "ignored") is null, "invalid admin hash does not fall back to plaintext");
        admin.PasswordHash = new PasswordHasher<AdminSettings>().HashPassword(admin, "SyntheticAdmin@123");
        var settings = new Dictionary<string, string?> {
            ["Admin:Username"] = admin.Username, ["Admin:PasswordHash"] = admin.PasswordHash, ["Admin:JwtKey"] = admin.JwtKey,
            ["Admin:Issuer"] = "test", ["Admin:Audience"] = "test", ["MongoDb:ConnectionString"] = "mongodb://synthetic:synthetic@127.0.0.1:27017",
            ["MongoDb:DatabaseName"] = "Synthetic", ["Uploads:Provider"] = "Cloudinary",
            ["Cloudinary:CloudName"] = "synthetic", ["Cloudinary:ApiKey"] = "synthetic-key", ["Cloudinary:ApiSecret"] = "synthetic-secret",
            ["Cors:AllowedOrigins:0"] = "https://www.weldingthozhilalarsangam.com"
        };
        var environment = new TestEnvironment();
        IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        ProductionConfiguration.Validate(Config(settings), environment);
        check(true, "complete production configuration validates without touching storage/database");
        foreach (var value in new[] { new string('k', 64), "REPLACE_WITH_AT_LEAST_32_RANDOM_BYTES" })
        {
            var weak = new Dictionary<string, string?>(settings) { ["Admin:JwtKey"] = value };
            try { ProductionConfiguration.Validate(Config(weak), environment); throw new Exception("Obvious weak signing key passed"); }
            catch (InvalidOperationException) { check(true, "production rejects repeated/template signing keys"); }
        }
        foreach (var key in new[] { "Admin:JwtKey", "Admin:PasswordHash", "MongoDb:ConnectionString", "Uploads:Provider", "Cloudinary:CloudName", "Cloudinary:ApiKey", "Cloudinary:ApiSecret", "Cors:AllowedOrigins:0" })
        {
            var invalid = new Dictionary<string, string?>(settings) { [key] = key == "MongoDb:ConnectionString" ? "mongodb://localhost" : "" };
            try { ProductionConfiguration.Validate(Config(invalid), environment); throw new Exception("Unsafe production configuration passed"); }
            catch (InvalidOperationException) { check(true, "production guard rejects missing " + key); }
        }
        var wildcard = new Dictionary<string, string?>(settings) { ["Cors:AllowedOrigins:0"] = "https://*.example.test" };
        try { ProductionConfiguration.Validate(Config(wildcard), environment); throw new Exception("Wildcard passed"); }
        catch (InvalidOperationException) { check(true, "production CORS wildcard rejected"); }
        var localUploads = new Dictionary<string, string?>(settings) { ["Uploads:Provider"] = "Local" };
        try { ProductionConfiguration.Validate(Config(localUploads), environment); throw new Exception("Ephemeral uploads passed"); }
        catch (InvalidOperationException) { check(true, "production rejects ephemeral local uploads"); }
        var logger = new SafeLogger();
        var middleware = new ErrorHandlingMiddleware(_ => throw new Exception("synthetic-private-secret"), logger);
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context); context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        check(context.Response.StatusCode == 500 && !body.Contains("synthetic-private-secret"), "generic API errors disclose no exception contents");
        check(!logger.Message.Contains("synthetic-private-secret") && logger.Exception is null && logger.Message.Contains("Exception"), "error logging retains type/trace only, without private exception text");
    }
    sealed class RefusingSender : ISmsSender { public Task<bool> SendOtpAsync(string p, string o, CancellationToken c = default) => Task.FromResult(false); }
    sealed class SafeLogger : ILogger<ErrorHandlingMiddleware>
    {
        public string Message = ""; public Exception? Exception;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { Message = formatter(state, exception); Exception = exception; }
    }
    sealed class CancellingSender : ISmsSender { public Task<bool> SendOtpAsync(string p, string o, CancellationToken c = default) => throw new OperationCanceledException(); }
    sealed class Clock : TimeProvider { DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance(TimeSpan span) => now += span; }
    sealed class RejectNetwork : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; throw new Exception("No real network allowed"); }
    }
    sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "ReadinessTests";
        public string ContentRootPath { get; set; } = Path.GetFullPath("artifacts/readiness-config-tests");
        public string WebRootPath { get; set; } = Path.GetFullPath("artifacts/readiness-config-tests/wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
