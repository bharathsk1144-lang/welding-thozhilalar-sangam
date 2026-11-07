using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed class AdminAuthService
{
    private readonly AdminSettings _settings;

    public AdminAuthService(IOptions<AdminSettings> settings)
    {
        _settings = settings.Value;
    }

    public (string Token, DateTime ExpiresAt)? Login(string username, string password)
    {
        var isConfiguredUser = string.Equals(username, _settings.Username, StringComparison.OrdinalIgnoreCase);
        var isConfiguredEmail = !string.IsNullOrWhiteSpace(_settings.Email) &&
            string.Equals(username, _settings.Email, StringComparison.OrdinalIgnoreCase);

        var validPassword = false;
        if (!string.IsNullOrEmpty(_settings.PasswordHash))
        {
            try { validPassword = new PasswordHasher<AdminSettings>().VerifyHashedPassword(_settings, _settings.PasswordHash, password) != PasswordVerificationResult.Failed; }
            catch (FormatException) { /* Invalid configuration fails closed. */ }
            catch (ArgumentException) { /* Invalid hash structure fails closed. */ }
        }
        else if (!string.IsNullOrEmpty(_settings.Password))
            validPassword = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), SHA256.HashData(Encoding.UTF8.GetBytes(_settings.Password)));
        if ((!isConfiguredUser && !isConfiguredEmail) || !validPassword)
        {
            return null;
        }

        var expiresAt = DateTime.UtcNow.AddHours(_settings.ExpiryHours);
        var adminName = isConfiguredEmail ? _settings.Email : _settings.Username;
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, adminName),
            new Claim(ClaimTypes.Role, "Admin")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
