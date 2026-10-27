using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentProcessingLeaseService
    {
        Task<ClaimedDocument?> ClaimNextAsync(CancellationToken cancellationToken);
        Task<bool> RenewAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken);
        Task<bool> OwnsLeaseAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken);
        Task<bool> MarkChunkedAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken);
        Task<bool> CompleteAsync(Guid documentId, Guid leaseId, DocumentStatus status, string? error, CancellationToken cancellationToken);
        Task<int> RecoverExpiredLeasesAsync(CancellationToken cancellationToken);
        Task<bool> TryQueueRetryAsync(Guid documentId, int userId, CancellationToken cancellationToken);

    }
}
