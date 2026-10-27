using Lexis.API.Data;
using Lexis.Core.DTOs.DocumentProcessing;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;

namespace Lexis.Core.Services
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IDocumentExtractionService _documentExtractionService;
        private readonly IDocumentPreprocessingService _preprocessingService;
        private readonly IDocumentChunkingService _documentChunkingService;
        private readonly IDocumentChunkRepository _documentChunkRepository;
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStoreService _vectorStoreService;
        private readonly IFileStorageService _fileStorageService;
        private readonly IDocumentProcessingLeaseService _leaseService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<DocumentProcessingOptions> _processingOptions;
        private readonly IDocumentStatusNotifier _documentStatusNotifier;

        public DocumentProcessingService(ApplicationDbContext context, ILogger<DocumentProcessingService> logger,
            IDocumentExtractionService documentExtractionService, IDocumentPreprocessingService preprocessingService,
            IDocumentChunkingService documentChunkingService, IDocumentChunkRepository documentChunkRepository,
            IEmbeddingService embeddingService, IVectorStoreService vectorStoreService, IFileStorageService fileStorageService,
            IDocumentProcessingLeaseService leaseService, IServiceScopeFactory scopeFactory, 
            IOptions<DocumentProcessingOptions> processingOptions, IDocumentStatusNotifier documentStatusNotifier)
        {
            _context = context;
            _logger = logger;
            _documentExtractionService = documentExtractionService;
            _preprocessingService = preprocessingService;
            _documentChunkingService = documentChunkingService;
            _documentChunkRepository = documentChunkRepository;
            _embeddingService = embeddingService;
            _vectorStoreService = vectorStoreService;
            _fileStorageService = fileStorageService;
            _leaseService = leaseService;
            _scopeFactory = scopeFactory;
            _processingOptions = processingOptions;
            _documentStatusNotifier = documentStatusNotifier;
        }

        public async Task ProcessPendingDocumentsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var claim = await _leaseService.ClaimNextAsync(cancellationToken);

                if (claim is null)
                    return;

                await ProcessClaimedDocumentAsync(claim, cancellationToken);
            }
        }

        public async Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken)
        {
            _logger.LogWarning(
                "Direct processing of document {DocumentId} is not supported " +
                "by the current lease service. The document must be claimed " +
                "through ProcessPendingDocumentsAsync.",
                documentId);

            await ProcessPendingDocumentsAsync(cancellationToken);
        }

        private async Task ProcessClaimedDocumentAsync(ClaimedDocument claim, CancellationToken cancellationToken)
        {
            var documentId = claim.DocumentId;
            var leaseId = claim.LeaseId;

            using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var renewalTask = RenewLeaseUntilCancelledAsync(documentId, leaseId, processingCts.Token);

            var document = await _context.Documents
                .FirstOrDefaultAsync(
                    d => d.Id == documentId,
                    cancellationToken);

            if (document is null)
            {
                _logger.LogWarning(
                    "Claimed document {DocumentId} was not found.",
                    documentId);

                return;
            }

            try
            {

                // The claim has already set the database status to Processing.
                await _documentStatusNotifier.NotifyAsync(claim.UploadedBy, documentId, claim.FileName, DocumentStatus.Processing,
                    cancellationToken: cancellationToken);

                // Remove data left behind by any previous processing attempt.
                await ReconcilePreviousAttemptAsync(documentId, leaseId, cancellationToken);

                // 1. Extract document content from R2 using a temporary local file.
                var extractedDocument = await ExtractDocumentFromR2Async(document.FilePath, cancellationToken);


                cancellationToken.ThrowIfCancellationRequested();

                // 2. Normalize document content.
                var processedDocument = await _preprocessingService.ProcessAsync(extractedDocument, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // 3. Chunk the document.
                var chunks = _documentChunkingService.CreateChunks(documentId, processedDocument);

                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException($"Document {documentId} produced no chunks to index.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 4. Persist chunks in PostgreSQL.
                await _documentChunkRepository.AddRangeAsync(chunks, cancellationToken);

                var markedChunked = await _leaseService.MarkChunkedAsync(documentId, leaseId, cancellationToken);

                if (!markedChunked)
                {
                    throw new DocumentProcessingLeaseLostException(documentId,
                        leaseId);
                }

                await _documentStatusNotifier.NotifyAsync(claim.UploadedBy, claim.DocumentId, claim.FileName,
                    DocumentStatus.Chunked, cancellationToken: cancellationToken); 

                cancellationToken.ThrowIfCancellationRequested();

                // 6. Generate one embedding per chunk.
                var texts = chunks
                    .Select(chunk => chunk.Content)
                    .ToArray();

                var embeddings = await _embeddingService.GenerateAsync(texts, cancellationToken);

                if (embeddings.Count != chunks.Count)
                {
                    throw new InvalidOperationException("The embedding service returned an unexpected number of vectors.");
                }

                // 7. Ensure the Qdrant collection exists.
                await _vectorStoreService.EnsureCollectionExistsAsync(cancellationToken);

                // 8. Store vectors in Qdrant.
                await _vectorStoreService.StoreAsync(chunks, embeddings, cancellationToken);

                // 9. Persist Qdrant vector IDs in PostgreSQL.
                foreach (var chunk in chunks)
                {
                    chunk.QdrantVectorId = chunk.Id.ToString("D");
                }

                await _context.SaveChangesAsync(cancellationToken);

                // 10. Mark the document Indexed only after indexing succeeds.
                var completed = await _leaseService.CompleteAsync(documentId, leaseId, DocumentStatus.Indexed, error: null,
                    cancellationToken);

                if (!completed)
                {
                    throw new DocumentProcessingLeaseLostException(documentId,leaseId);
                }

                await _documentStatusNotifier.NotifyAsync(claim.UploadedBy, documentId, claim.FileName, DocumentStatus.Indexed,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Successfully processed document {DocumentId}.",
                    documentId);
            }
            catch (OperationCanceledException)
            {
                // Leave the document in Processing.
                // Recovery must decide whether the processing attempt is abandoned.
                _logger.LogInformation(
                    "Processing cancelled for document {DocumentId}.",
                    documentId);

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed processing document {DocumentId}.",
                    documentId);

                try
                {
                    // Only the current lease owner may mark the document Failed.
                    var markedFailed = await _leaseService.CompleteAsync(documentId, leaseId, DocumentStatus.Failed, ex.Message,
                        CancellationToken.None);

                    if (!markedFailed)
                    {
                        _logger.LogWarning(
                            "Could not mark document {DocumentId} as Failed because " +
                            "the processing lease is no longer valid. Another attempt " +
                            "may own the document or it may already be complete.",
                            documentId);

                        return;
                    }

                    await _documentStatusNotifier.NotifyAsync(claim.UploadedBy, documentId, claim.FileName, DocumentStatus.Failed,
                        error: ex.Message, cancellationToken: CancellationToken.None);
                }
                catch (Exception failureHandlingException)
                {
                    _logger.LogError(
                        failureHandlingException,
                        "Could not persist or publish the Failed status for document {DocumentId}.",
                        documentId);
                }
            }
        }

        private async Task<ExtractedDocument> ExtractDocumentFromR2Async(string objectKey, CancellationToken cancellationToken)
        {
            // Use a unique directory for each processing attempt.
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"lexis-{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDirectory);

                var tempPdfPath = Path.Combine(tempDirectory, "document.pdf");

                // Download the persistent PDF from R2.
                await using (var sourceStream = await _fileStorageService.GetFileAsync(objectKey))
                {
                    await using var destinationStream = new FileStream(tempPdfPath, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, bufferSize: 81920, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

                    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Docling continues using the existing file-path interface.
                return await _documentExtractionService.ExtractAsync(tempPdfPath, cancellationToken);
            }
            finally
            {
                // Cleanup must not replace an extraction, download, or cancellation exception.
                // If cleanup fails, log it so the leftover files can be investigated.
                try
                {
                    if (Directory.Exists(tempDirectory))
                    {
                        Directory.Delete(tempDirectory, recursive: true);
                    }
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(cleanupException, "Could not clean up temporary document directory {TempDirectory}.", tempDirectory);
                }
            }
        }

        private async Task<Guid?> ClaimNextPendingDocumentAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var documentId = await _context.Documents
                    .AsNoTracking()
                    .Where(d => d.Status == DocumentStatus.Pending)
                    .OrderBy(d => d.UploadedAt)
                    .Select(d => (Guid?)d.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (documentId is null)
                    return null;

                var now = DateTime.UtcNow;
                var leaseId = Guid.NewGuid();

                var claimed = await _context.Documents
                    .Where(d =>
                        d.Id == documentId.Value &&
                        d.Status == DocumentStatus.Pending &&
                        d.ProcessingAttempt < 3)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(d => d.Status, DocumentStatus.Processing)
                        .SetProperty(d => d.ProcessingStartedAt, now)
                        .SetProperty(d => d.ProcessingCompletedAt, (DateTime?)null)
                        .SetProperty(d => d.ProcessingError, (string?)null)
                        .SetProperty(d => d.ProcessingLeaseId, leaseId)
                        .SetProperty(
                            d => d.ProcessingLeaseExpiresAt,
                            now.AddMinutes(5))
                        .SetProperty(
                            d => d.ProcessingAttempt,
                            d => d.ProcessingAttempt + 1),
                        cancellationToken);

                if (claimed == 1)
                    return documentId.Value;

                // It was claimed by another worker, or has exhausted its attempts.
            }

            return null;
        }

        private async Task RenewLeaseUntilCancelledAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(_processingOptions.Value.LeaseRenewalSeconds);

            while (true)
            {
                await Task.Delay(interval, cancellationToken);

                await using var scope = _scopeFactory.CreateAsyncScope();

                var leaseService = scope.ServiceProvider
                    .GetRequiredService<IDocumentProcessingLeaseService>();

                var renewed = await leaseService.RenewAsync(documentId, leaseId, cancellationToken);

                if (!renewed)
                {
                    throw new DocumentProcessingLeaseLostException(documentId, leaseId);
                }
            }
        }

        private async Task EnsureLeaseOwnedAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
        {
            var ownsLease = await _leaseService.OwnsLeaseAsync(documentId, leaseId, cancellationToken);

            if (!ownsLease)
            {
                throw new DocumentProcessingLeaseLostException(documentId, leaseId);
            }
        }

        private async Task ReconcilePreviousAttemptAsync(Guid documentId, Guid leaseId, CancellationToken cancellationToken)
        {
            await EnsureLeaseOwnedAsync(documentId, leaseId, cancellationToken);

            // Remove vectors first. If this fails, leave the PostgreSQL
            // chunks intact and let the attempt fail/retry.
            await _vectorStoreService.DeleteByDocumentIdAsync(documentId, cancellationToken);

            await EnsureLeaseOwnedAsync(documentId, leaseId, cancellationToken);

            await _documentChunkRepository.DeleteByDocumentIdAsync(documentId, cancellationToken);
        }
    }
}
