namespace Lexis.Core.DTOs.UsageLogs
{
    public class UsageLogPageDTO
    {
        public IReadOnlyList<UsageLogItemDTO> Items { get; set; } = Array.Empty<UsageLogItemDTO>();

        public UsageLogSummaryDTO Summary { get; set; } = new();

        public int Page { get; set; }

        public int PageSize { get; set; }

        public int TotalCount { get; set; }

        public int TotalPages =>
            (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
