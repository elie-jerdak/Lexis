namespace Lexis.Core.DTOs.UsageLogs
{
    public class UsageLogItemDTO
    {
        public Guid Id { get; set; }

        public int UserId { get; set; }

        public Guid? MessageId { get; set; }

        public DateTime RequestTime { get; set; }

        public int ResponseTimeMs { get; set; }

        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }

        public int TotalTokens => InputTokens + OutputTokens;

        public bool CacheHit { get; set; }
    }
}
