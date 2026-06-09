using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BackendApi.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateAccessToken(TokenSubject subject, DateTime expiresAtUtc)
    {
        var currentKeyId = _configuration["Jwt:CurrentKeyId"];
        string? jwtKey = null;

        if (!string.IsNullOrEmpty(currentKeyId))
        {
            jwtKey = _configuration[$"Jwt:Keys:{currentKeyId}"];
        }

        if (string.IsNullOrEmpty(jwtKey))
        {
            jwtKey = _configuration["Jwt:Key"];
            currentKeyId = "default";
        }

        if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
        {
            throw new InvalidOperationException("Valid JWT configuration is missing or too short.");
        }

        var claimsList = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, subject.UserId),
            new Claim(ClaimTypes.Email, subject.Email),
            new Claim(ClaimTypes.Name, subject.DisplayName),
            new Claim(ClaimTypes.Role, subject.Role)
        };
        if (!string.IsNullOrWhiteSpace(subject.ShopId))
        {
            claimsList.Add(new Claim("shop_id", subject.ShopId));
        }
        var claims = claimsList.ToArray();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        {
            KeyId = currentKeyId
        };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
