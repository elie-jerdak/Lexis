using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.User
{
    public class UserDashboardDTO
    {
        // User information
        public int Id { get; set; }

        public string FirstName { get; set; } = null!;

        public string LastName { get; set; } = null!;

        public string Email { get; set; } = null!;

        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; }


        // Document statistics
        public int TotalDocuments { get; set; }


        // Conversation statistics
        public int TotalConversations { get; set; }

        public int TotalMessages { get; set; }


        // Usage statistics
        public int TokensUsed { get; set; }

        public int TokenLimit { get; set; }


        public DateTime CreatedAt { get; set; }
    }
}
