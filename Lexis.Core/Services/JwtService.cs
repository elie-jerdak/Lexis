using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;  
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Lexis.Core.Services
{
    public class JwtService : IJwtService
    {
        private readonly JwtSettings _settings;

        public JwtService(
            IOptions<JwtSettings> settings)
        {
            _settings = settings.Value;
        }

        public string GenerateAccessToken(ApplicationUser user, IList<string> roles)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email!),
                new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}")
            };


            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));

            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires:DateTime.UtcNow.AddMinutes(
                        _settings.AccessTokenExpirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler()
                .WriteToken(token);
        }


        public RefreshToken GenerateRefreshToken(int userId)
        {
            return new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Token =
                    Convert.ToBase64String(
                        RandomNumberGenerator.GetBytes(64)
                    ),

                CreatedAt = DateTime.UtcNow,
                ExpiresAt =
                    DateTime.UtcNow.AddDays(
                        _settings.RefreshTokenExpirationDays)
            };
        }
    }
}
