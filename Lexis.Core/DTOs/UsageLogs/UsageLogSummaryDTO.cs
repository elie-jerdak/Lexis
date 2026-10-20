namespace Lexis.Core.DTOs.UsageLogs
{
    public class UsageLogSummaryDTO
    {
        public int TotalRequests { get; set; }

        public long TotalInputTokens { get; set; }

        public long TotalOutputTokens { get; set; }

        public long TotalTokens => TotalInputTokens + TotalOutputTokens;

        public double AverageResponseTimeMs { get; set; }

        public int CacheHits { get; set; }

        public double CacheHitRate { get; set; }
    }
}
