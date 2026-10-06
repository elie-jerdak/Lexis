using Lexis.API.Data;
using Lexis.Core.Enums;
using Lexis.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

        public DocumentProcessingService(ApplicationDbContext context, ILogger<DocumentProcessingService> logger,
            IDocumentExtractionService documentExtractionService, IDocumentPreprocessingService preprocessingService,
            IDocumentChunkingService documentChunkingService, IDocumentChunkRepository documentChunkRepository)
        {
            _context = context;
            _logger = logger;
            _documentExtractionService = documentExtractionService;
            _preprocessingService = preprocessingService;
            _documentChunkingService = documentChunkingService;
            _documentChunkRepository = documentChunkRepository;
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
                _logger.LogWarning(
                    "Document {DocumentId} was not found.",
                    documentId);

                return;
            }

            try
            {
                // Mark document as processing
                document.Status = DocumentStatus.Processing;
                document.ProcessingStartedAt = DateTime.UtcNow;
                document.ProcessingCompletedAt = null;
                document.ProcessingError = null;

                await _context.SaveChangesAsync(cancellationToken);


                // ======================================================
                // 1. Extract document content
                // ======================================================

                var extractedDocument = await _documentExtractionService.ExtractAsync(document.FilePath, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();


                // ======================================================
                // 2. Normalize document content
                // ======================================================

                var processedDocument = await _preprocessingService.ProcessAsync(extractedDocument, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // ======================================================
                // 3. Chunk document
                // ======================================================

                var chunks = _documentChunkingService.CreateChunks(documentId, processedDocument);

                cancellationToken.ThrowIfCancellationRequested();


                // ======================================================
                // 4. Save chunks
                // ======================================================

                await _documentChunkRepository.AddRangeAsync(chunks, cancellationToken);

                document.Status = DocumentStatus.Chunked;

                await _context.SaveChangesAsync(cancellationToken);


                // ======================================================
                // 5. Generate embeddings
                // ======================================================

                cancellationToken.ThrowIfCancellationRequested();

                // var embeddings = await _embeddingService.GenerateAsync(chunks, cancellationToken);


                // ======================================================
                // 6. Store vectors in Qdrant
                // ======================================================

                cancellationToken.ThrowIfCancellationRequested();

                // await _vectorStoreService.StoreAsync(embeddings, cancellationToken);


                // ======================================================
                // Completed
                // ======================================================

                document.Status = DocumentStatus.Indexed;
                document.ProcessingCompletedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully processed document {DocumentId}.",
                    documentId);
            }
            catch (OperationCanceledException ex)
            {
                // Check if the cancellation came from a specific token source
                _logger.LogInformation(
                    ex,
                    "Processing cancelled for document {DocumentId}. Inner message: {Message}",
                    documentId,
                    ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                document.Status = DocumentStatus.Failed;
                document.ProcessingCompletedAt = DateTime.UtcNow;
                document.ProcessingError = ex.Message;

                // Do not use cancellation token here.
                // We want to save failure information even if cancellation happened.
                await _context.SaveChangesAsync();

                _logger.LogError(
                    ex,
                    "Failed processing document {DocumentId}.",
                    documentId);
            }
        }
    }
}
