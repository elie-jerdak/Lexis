using Lexis.API.Data;
using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lexis.Core.Services;

public sealed class DocumentProcessingLeaseService : IDocumentProcessingLeaseService
{
    private readonly ApplicationDbContext _db;
    private readonly DocumentProcessingOptions _options;

    public DocumentProcessingLeaseService(ApplicationDbContext db, IOptions<DocumentProcessingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<ClaimedDocument?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        // Expired jobs that have exhausted their attempts must not
        // remain Processing indefinitely.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Documents"
            SET
                "Status" = 'failed'::document_status,
                "ProcessingCompletedAt" = NOW(),
                "ProcessingError" = 'Processing lease expired and maximum attempts were reached.',
                "ProcessingLeaseId" = NULL,
                "ProcessingLeaseExpiresAt" = NULL
            WHERE
                "Status" IN (
                    'processing'::document_status,
                    'chunked'::document_status
                )
                AND "ProcessingLeaseExpiresAt" <= NOW()
                AND "ProcessingAttempt" >= {_options.MaxAttempts};
            """, cancellationToken);

        // The CTE locks one eligible row and skips rows another worker
        // has already locked. The UPDATE and RETURNING are one statement.
        var claimed = await _db.Database
            .SqlQuery<ClaimedDocument>($"""
                WITH candidate AS
                (
                    SELECT d."Id"
                    FROM "Documents" AS d
                    WHERE
                        d."ProcessingAttempt" < {_options.MaxAttempts}
                        AND
                        (
                            d."Status" = 'pending'::document_status
                            OR
                            (
                                d."Status" IN (
                                    'processing'::document_status,
                                    'chunked'::document_status
                                )
                                AND d."ProcessingLeaseExpiresAt" <= NOW()
                            )
                        )
                    ORDER BY d."UploadedAt" ASC, d."Id" ASC
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                ),
                claimed AS
                (
                    UPDATE "Documents" AS d
                    SET
                        "Status" = 'processing'::document_status,
                        "ProcessingStartedAt" = NOW(),
                        "ProcessingCompletedAt" = NULL,
                        "ProcessingError" = NULL,
                        "ProcessingLeaseId" = gen_random_uuid(),
                        "ProcessingLeaseExpiresAt" =
                            NOW() + make_interval(secs => {_options.LeaseDurationSeconds}),
                        "ProcessingAttempt" = d."ProcessingAttempt" + 1
                    FROM candidate AS c
                    WHERE d."Id" = c."Id"
                    RETURNING
                        d."Id" AS "DocumentId",
                        d."UploadedBy" AS "UploadedBy",
                        d."FileName" AS "FileName",
                        d."ProcessingLeaseId" AS "LeaseId",
                        d."ProcessingAttempt" AS "ProcessingAttempt"
                )
                SELECT
                    "DocumentId",
                    "UploadedBy",
                    "FileName",
                    "LeaseId",
                    "ProcessingAttempt"
                FROM claimed
                """)
            .ToListAsync(cancellationToken);

        return claimed.FirstOrDefault();
    }

    public async Task<bool> RenewAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
    {
        var affected = await _db.Documents
            .Where(d =>
                    d.Id == documentId &&
                    (d.Status == DocumentStatus.Processing ||
                     d.Status == DocumentStatus.Chunked) &&
                    d.ProcessingLeaseId == leaseId &&
                    d.ProcessingLeaseExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    d => d.ProcessingLeaseExpiresAt,
                    DateTime.UtcNow.AddSeconds(
                        _options.LeaseDurationSeconds)),
                cancellationToken);

        return affected == 1;
    }

    public async Task<bool> OwnsLeaseAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
    {
        return await _db.Documents
            .AsNoTracking()
            .AnyAsync(d =>
                    d.Id == documentId &&
                    (d.Status == DocumentStatus.Processing ||
                     d.Status == DocumentStatus.Chunked) &&
                    d.ProcessingLeaseId == leaseId &&
                    d.ProcessingLeaseExpiresAt > DateTime.UtcNow,
                    cancellationToken);
    }

    public async Task<bool> CompleteAsync(Guid documentId, Guid leaseId, DocumentStatus status, string? error,
        CancellationToken cancellationToken)
    {
        if (status is not DocumentStatus.Indexed and not DocumentStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "Only Indexed and Failed are terminal processing statuses.");
        }

        var affected = await _db.Documents
            .Where(d => d.Id == documentId && 
                    (d.Status == DocumentStatus.Processing || d.Status == DocumentStatus.Chunked) &&
                     d.ProcessingLeaseId == leaseId && d.ProcessingLeaseExpiresAt > DateTime.UtcNow)
                .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, status)
                .SetProperty(
                    d => d.ProcessingCompletedAt,
                    status == DocumentStatus.Indexed ||
                    status == DocumentStatus.Failed
                        ? DateTime.UtcNow
                        : (DateTime?)null)
                .SetProperty(d => d.ProcessingError, error)
                .SetProperty(d => d.ProcessingLeaseId, (Guid?)null)
                .SetProperty(
                    d => d.ProcessingLeaseExpiresAt,
                    (DateTime?)null),
                cancellationToken);

        return affected == 1;
    }

    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        // A lease that has not exhausted its attempts remains eligible
        // for ClaimNextAsync. Only exhausted jobs are marked Failed here.
        return await _db.Documents
            .Where(d =>
                    (d.Status == DocumentStatus.Processing ||
                     d.Status == DocumentStatus.Chunked) &&
                    d.ProcessingLeaseExpiresAt <= DateTime.UtcNow &&
                    d.ProcessingAttempt >= _options.MaxAttempts)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, DocumentStatus.Failed)
                .SetProperty(
                    d => d.ProcessingCompletedAt,
                    DateTime.UtcNow)
                .SetProperty(
                    d => d.ProcessingError,
                    "Processing lease expired and maximum attempts were reached.")
                .SetProperty(d => d.ProcessingLeaseId, (Guid?)null)
                .SetProperty(
                    d => d.ProcessingLeaseExpiresAt,
                    (DateTime?)null),
                cancellationToken);
    }

    public async Task<bool> MarkChunkedAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var affected = await _db.Documents
            .Where(d =>
                d.Id == documentId &&
                d.Status == DocumentStatus.Processing &&
                d.ProcessingLeaseId == leaseId &&
                d.ProcessingLeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, DocumentStatus.Chunked)
                .SetProperty(d => d.ProcessingError, (string?)null),
                cancellationToken);

        return affected == 1;
    }

    public async Task<bool> TryQueueRetryAsync(
    Guid documentId,
    int userId,
    CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var affected = await _db.Documents
            .Where(d =>
                d.Id == documentId &&
                d.UploadedBy == userId &&
                (
                    d.Status == DocumentStatus.Failed ||
                    (
                        (d.Status == DocumentStatus.Processing ||
                         d.Status == DocumentStatus.Chunked) &&
                        d.ProcessingLeaseExpiresAt != null &&
                        d.ProcessingLeaseExpiresAt <= now
                    )
                ))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, DocumentStatus.Pending)
                .SetProperty(d => d.ProcessingAttempt, 0)
                .SetProperty(
                    d => d.ProcessingStartedAt,
                    (DateTime?)null)
                .SetProperty(
                    d => d.ProcessingCompletedAt,
                    (DateTime?)null)
                .SetProperty(
                    d => d.ProcessingError,
                    (string?)null)
                .SetProperty(
                    d => d.ProcessingLeaseId,
                    (Guid?)null)
                .SetProperty(
                    d => d.ProcessingLeaseExpiresAt,
                    (DateTime?)null),
                cancellationToken);

        return affected == 1;
    }
}