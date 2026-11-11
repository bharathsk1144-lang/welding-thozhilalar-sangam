using System.Text;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;
        var admin = configuration.GetSection("Admin").Get<AdminSettings>() ?? new();
        if (Encoding.UTF8.GetByteCount(admin.JwtKey) < 32 || admin.JwtKey.Distinct().Count() < 8 ||
            admin.JwtKey.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase) ||
            admin.JwtKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configure a strong production Admin:JwtKey (at least 32 random bytes).");
        if (string.IsNullOrWhiteSpace(admin.Username) || string.IsNullOrWhiteSpace(admin.PasswordHash) ||
            string.IsNullOrWhiteSpace(admin.Issuer) || string.IsNullOrWhiteSpace(admin.Audience) || admin.ExpiryHours is < 1 or > 24)
            throw new InvalidOperationException("Production requires admin identity, an Identity password hash, JWT issuer/audience and a bounded session duration.");
        try
        {
            var hash = Convert.FromBase64String(admin.PasswordHash);
            if (hash.Length < 61 || hash[0] != 1) throw new FormatException();
        }
        catch (FormatException) { throw new InvalidOperationException("Admin:PasswordHash must be a valid Identity password hash."); }
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo) || origin.Contains('*')))
            throw new InvalidOperationException("Configure exact HTTPS production CORS origins.");
        var mongo = new MongoUrl(configuration["MongoDb:ConnectionString"] ?? "mongodb://localhost");
        if (string.IsNullOrWhiteSpace(mongo.Username) || string.IsNullOrWhiteSpace(mongo.Password) ||
            string.IsNullOrWhiteSpace(configuration["MongoDb:DatabaseName"]))
            throw new InvalidOperationException("Production requires an authenticated MongoDB connection and explicit database name.");
        if (!string.Equals(configuration["Uploads:Provider"], "Cloudinary", StringComparison.OrdinalIgnoreCase) ||
            new[] { "Cloudinary:CloudName", "Cloudinary:ApiKey", "Cloudinary:ApiSecret" }
                .Any(key => string.IsNullOrWhiteSpace(configuration[key]) || configuration[key]!.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Production requires Cloudinary authenticated private storage credentials; local uploads are ephemeral.");
    }
}
