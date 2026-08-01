using Lexis.API.Data;
using Lexis.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly ApplicationDbContext _context;

        public RefreshTokenService(ApplicationDbContext context)
        {
            _context = context;
        }


        public async Task RevokeUserRefreshTokensAsync(int userId)
        {
            var activeTokens = await _context.RefreshTokens
                .Where(rt =>
                    rt.UserId == userId &&
                    rt.RevokedAt == null &&
                    rt.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();


            foreach (var token in activeTokens)
            {
                token.Revoke();
            }


            await _context.SaveChangesAsync();
        }
    }
}
