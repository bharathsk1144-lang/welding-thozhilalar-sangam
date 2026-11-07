using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed class UserSessionService(IOptions<AdminSettings> options)
{
    public (string Token, DateTime ExpiresAt) Create(UserRegistration user)
    {
        var settings = options.Value;
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience,
            [new Claim(ClaimTypes.NameIdentifier, user.Id!), new Claim(ClaimTypes.Role, "Member")],
            expires: expiresAt,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtKey)),
                SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
