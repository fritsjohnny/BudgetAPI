using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BudgetAPI.Models;
using Microsoft.IdentityModel.Tokens;

namespace BudgetAPI.Authorization
{
    public interface IJwtUtils
    {
        string GenerateToken(Users user);
        int? ValidateToken(string? token, out long passwordVersion);
    }

    public class JwtUtils : IJwtUtils
    {
        private readonly IConfiguration _configuration;

        public JwtUtils(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private string GetSecret()
        {
            string? secret = _configuration["BUDGETAPI_SECRET"];
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("JWT Secret não está configurado. Configure a chave 'BUDGETAPI_SECRET' em secrets.json");
            return secret;
        }

        public string GenerateToken(Users user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(GetSecret());
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("id", user.Id.ToString()),
                    new Claim("pwd", user.PasswordChangedAt?.Ticks.ToString() ?? "0")
                }),
                Expires = DateTime.UtcNow.AddDays(7),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public int? ValidateToken(string? token, out long passwordVersion)
        {
            passwordVersion = 0;
            if (token == null)
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(GetSecret());
            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                int userId = int.Parse(jwtToken.Claims.First(x => x.Type == "id").Value);
                string? versionClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "pwd")?.Value;
                if (!string.IsNullOrEmpty(versionClaim) && !long.TryParse(versionClaim, out passwordVersion))
                    return null;

                return userId;
            }
            catch
            {
                return null;
            }
        }
    }
}
