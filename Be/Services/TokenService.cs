using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Be.Infrastructure;
using Be.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Be.Services;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JwtOptions _options = options.Value;

    public static SymmetricSecurityKey CreateKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    public string Issue(string subject, string sessionId, string role, Guid? deviceLinkId = null)
    {
        var claims = new List<Claim>
        {
            new(GameClaims.Subject, subject),
            new(GameClaims.SessionId, sessionId),
            new(GameClaims.Role, role),
        };
        if (deviceLinkId is { } link)
        {
            claims.Add(new Claim(GameClaims.DeviceLink, link.ToString()));
        }

        var now = time.UtcNow();
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            notBefore: now,
            expires: now.AddHours(_options.TokenLifetimeHours),
            signingCredentials: new SigningCredentials(CreateKey(_options), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
