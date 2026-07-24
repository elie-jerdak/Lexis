using Microsoft.AspNetCore.Identity;

namespace Lexis.Core.Models
{
    public class ApplicationUser : IdentityUser<int>
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;
        public int MonthlyTokenAllowance { get; set; } = 100000;
    }
}
