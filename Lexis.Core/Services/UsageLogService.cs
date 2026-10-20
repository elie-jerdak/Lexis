
using Lexis.API.Data;
using Lexis.Core.DTOs.UsageLogs;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services;

public sealed class UsageLogService : IUsageLogService
{
    private readonly ApplicationDbContext _dbContext;

    public UsageLogService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UsageLogPageDTO> GetUsageLogsAsync(int authenticatedUserId, bool isAdmin, UsageLogQueryDTO query,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var from = query.From ?? now.AddDays(-30);
        var to = query.To ?? now;

        if (from > to)
        {
            throw new ArgumentException(
                "'From' must be earlier than or equal to 'To'.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        IQueryable<UsageLog> logs = _dbContext.UsageLogs
            .AsNoTracking()
            .Where(log =>
                log.RequestTime >= from &&
                log.RequestTime <= to);

        // Non-admin users can only retrieve their own logs.
        if (!isAdmin)
        {
            logs = logs.Where(log =>
                log.UserId == authenticatedUserId);
        }
        else if (query.UserId.HasValue)
        {
            logs = logs.Where(log =>
                log.UserId == query.UserId.Value);
        }

        var totalCount = await logs.CountAsync(cancellationToken);

        var aggregate = await logs
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalRequests = group.Count(),
                TotalInputTokens = group.Sum(log => (long)log.InputTokens),
                TotalOutputTokens = group.Sum(log => (long)log.OutputTokens),
                AverageResponseTimeMs = group.Average(
                    log => (double)log.ResponseTimeMs),
                CacheHits = group.Count(log => log.CacheHit)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var summary = new UsageLogSummaryDTO
        {
            TotalRequests = aggregate?.TotalRequests ?? 0,
            TotalInputTokens = aggregate?.TotalInputTokens ?? 0,
            TotalOutputTokens = aggregate?.TotalOutputTokens ?? 0,
            AverageResponseTimeMs =
                aggregate?.AverageResponseTimeMs ?? 0,
            CacheHits = aggregate?.CacheHits ?? 0,
            CacheHitRate = aggregate is { TotalRequests: > 0 }
                ? aggregate.CacheHits * 100.0 / aggregate.TotalRequests
                : 0
        };

        var items = await logs
            .OrderByDescending(log => log.RequestTime)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new UsageLogItemDTO
            {
                Id = log.Id,
                UserId = log.UserId,
                MessageId = log.MessageId,
                RequestTime = log.RequestTime,
                ResponseTimeMs = log.ResponseTimeMs,
                InputTokens = log.InputTokens,
                OutputTokens = log.OutputTokens,
                CacheHit = log.CacheHit
            })
            .ToListAsync(cancellationToken);

        return new UsageLogPageDTO
        {
            Items = items,
            Summary = summary,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
