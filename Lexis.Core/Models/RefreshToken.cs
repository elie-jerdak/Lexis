using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Models
{
    public class RefreshToken
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        public string Token { get; set; } = null!;

        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? RevokedAt { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

        public bool IsRevoked => RevokedAt != null;

        public bool IsActive => !IsExpired && !IsRevoked;

        public void Revoke()
        {
            RevokedAt = DateTime.UtcNow;
        }

        public ApplicationUser User { get; set; } = null!;
    }
}
