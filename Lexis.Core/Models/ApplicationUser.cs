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
        public int TokensConsumedCurrentMonth { get; set; } = 0;
        // Rate limiting
        public DateTime? LastRequestTimestamp { get; set; }
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

        public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

        public virtual ICollection<UsageLog> UsageLogs { get; set; } = new List<UsageLog>();
    }
}
