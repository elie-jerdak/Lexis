namespace Lexis.Core.DTOs.Users
{
    public class UserProfileDTO
    {
        public int Id { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public int MonthlyTokenAllowance { get; set; }

        public int TokensConsumedCurrentMonth { get; set; }
    }
}
