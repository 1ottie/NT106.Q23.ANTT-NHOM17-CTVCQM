using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

public class JwtHelper
{
    private readonly string secret;
    private readonly int expireHours;

    public JwtHelper(IConfiguration config)
    {
        secret = config["Jwt:Secret"];
        expireHours = int.Parse(config["Jwt:ExpireHours"]);
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("user_id", user.user_id.ToString()),
            new Claim("username", user.username)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.Now.AddHours(expireHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}