namespace Lexis.Core.DTOs.UsageLogs;

public sealed class UsageLogQueryDTO
{
    public DateTime? From { get; set; }

    public DateTime? To { get; set; }

    public int? UserId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}