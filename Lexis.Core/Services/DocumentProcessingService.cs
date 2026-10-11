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
        private readonly IEmbeddingService _embeddingService;
        private readonly IVectorStoreService _vectorStoreService;

        public DocumentProcessingService(ApplicationDbContext context, ILogger<DocumentProcessingService> logger,
            IDocumentExtractionService documentExtractionService, IDocumentPreprocessingService preprocessingService,
            IDocumentChunkingService documentChunkingService, IDocumentChunkRepository documentChunkRepository,
            IEmbeddingService embeddingService, IVectorStoreService vectorStoreService)
        {
            _context = context;
            _logger = logger;
            _documentExtractionService = documentExtractionService;
            _preprocessingService = preprocessingService;
            _documentChunkingService = documentChunkingService;
            _documentChunkRepository = documentChunkRepository;
            _embeddingService = embeddingService;
            _vectorStoreService = vectorStoreService;
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

                // The chunks must already exist in PostgreSQL.
                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Document {documentId} produced no chunks to index.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 1. Generate one embedding per chunk.
                var texts = chunks
                    .Select(chunk => chunk.Content)
                    .ToArray();

                var embeddings = await _embeddingService.GenerateAsync(texts, cancellationToken);

                if (embeddings.Count != chunks.Count)
                {
                    throw new InvalidOperationException(
                        "The embedding service returned an unexpected number of vectors.");
                }

                // 2. Ensure the collection exists with the configured vector size.
                await _vectorStoreService.EnsureCollectionExistsAsync(cancellationToken);

                // ======================================================================
                // 6. Store embeddings in Qdrant and update chunk records with Qdrant IDs
                // ======================================================================

                // 1. Store the embeddings in Qdrant.
                await _vectorStoreService.StoreAsync(chunks, embeddings, cancellationToken);

                // 2. Persist the Qdrant IDs only after the upsert succeeds.
                foreach (var chunk in chunks)
                {
                    chunk.QdrantVectorId = chunk.Id.ToString("D");
                }

                await _context.SaveChangesAsync(cancellationToken);

                // ======================================================
                // 7. Completed
                // ======================================================

                // Mark the document Indexed only after both stores succeed.
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
