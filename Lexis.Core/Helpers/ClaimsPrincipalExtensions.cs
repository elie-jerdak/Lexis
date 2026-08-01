using System.Security.Claims;

namespace Lexis.Core.Helpers
{
    public static class ClaimsPrincipalExtensions
    {
        public static int? GetUserId(this ClaimsPrincipal user)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            return int.TryParse(userId, out var id)
                ? id
                : null;
        }
    }
}
