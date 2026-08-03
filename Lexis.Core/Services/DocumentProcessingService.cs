using Lexis.API.Data;
using Lexis.Core.Enums;
using Lexis.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Services
{
    public class DocumentProcessingService : IDocumentProcessingService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DocumentProcessingService> _logger;
        private readonly IDocumentExtractionService _documentExtractionService;
        private readonly IDocumentPreprocessingService _preprocessingService;

        public DocumentProcessingService(ApplicationDbContext context, ILogger<DocumentProcessingService> logger,
            IDocumentExtractionService documentExtractionService, IDocumentPreprocessingService preprocessingService)
        {
            _context = context;
            _logger = logger;
            _documentExtractionService = documentExtractionService;
            _preprocessingService = preprocessingService;
        }

        public async Task ProcessPendingDocumentsAsync(CancellationToken cancellationToken)
        {
            // Recover documents that were interrupted (application crash, deployment, etc.)
            var timeout = DateTime.UtcNow.AddMinutes(-30);

            var stuckDocuments = await _context.Documents
                .Where(d =>
                    d.Status == DocumentStatus.Processing &&
                    d.ProcessingStartedAt != null &&
                    d.ProcessingStartedAt < timeout)
                .ToListAsync(cancellationToken);

            foreach (var document in stuckDocuments)
            {
                document.Status = DocumentStatus.Pending;
                document.ProcessingStartedAt = null;
                document.ProcessingCompletedAt = null;
                document.ProcessingError = "Processing timed out. Retrying.";
            }

            if (stuckDocuments.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            var documents = await _context.Documents
                .Where(d => d.Status == DocumentStatus.Pending)
                .ToListAsync(cancellationToken);

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ProcessDocumentAsync(document.Id, cancellationToken);
            }
        }

        public async Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken)
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(
                    d => d.Id == documentId,
                    cancellationToken);

            if (document == null)
            {
                _logger.LogWarning("Document {DocumentId} was not found.", documentId);
                return;
            }

            try
            {
                document.Status = DocumentStatus.Processing;
                document.ProcessingStartedAt = DateTime.UtcNow;
                document.ProcessingCompletedAt = null;
                document.ProcessingError = null;

                await _context.SaveChangesAsync(cancellationToken);

                // ======================================================
                // Pipeline
                // ======================================================

                try
                {
                    // 1. Extract document content

                    var extractedDocument = await _documentExtractionService.ExtractAsync(document.FilePath, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    // 2. Normalize document content

                    var processedPages = await _preprocessingService.ProcessAsync(extractedDocument.Pages, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    // 3. Chunk document

                    cancellationToken.ThrowIfCancellationRequested();

                    // 4. Generate embeddings

                    cancellationToken.ThrowIfCancellationRequested();

                    // 5. Store vectors in Qdrant

                    cancellationToken.ThrowIfCancellationRequested();

                    // ====================================================== 
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "Extraction service error while processing document {DocumentId}.", documentId);
                    throw; // Rethrow to let the outer catch update database status to Failed
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pipeline processing error while processing document {DocumentId}.", documentId);
                    throw; // Rethrow to let the outer catch update database status to Failed
                }

                document.Status = DocumentStatus.Indexed;
                document.ProcessingCompletedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully processed document {DocumentId}.", documentId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Processing cancelled for document {DocumentId}.", documentId);
                throw;
            }
            catch (Exception ex)
            {
                document.Status = DocumentStatus.Failed;
                document.ProcessingCompletedAt = DateTime.UtcNow;
                document.ProcessingError = ex.Message;

                await _context.SaveChangesAsync();

                _logger.LogError(ex, "Failed processing document {DocumentId}.", documentId);
            }
        }
    }
}
