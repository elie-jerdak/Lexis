using Lexis.API.Data;
using Lexis.Core.DTOs.Document;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore; 

namespace Lexis.Core.Services
{
    public class DocumentService : IDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileStorageService _fileStorageService;
        private readonly IFileHashService _fileHashService;
        private readonly IDocumentProcessingLeaseService _processingLeaseService;

        public DocumentService(ApplicationDbContext context, IFileStorageService fileStorageService,
            IFileHashService fileHashService, IDocumentProcessingLeaseService processingLeaseService)
        {
            _context = context;
            _fileStorageService = fileStorageService;
            _fileHashService = fileHashService;
            _processingLeaseService = processingLeaseService;
        }

        public async Task<ServiceResult<UploadDocumentResponseDTO>> UploadDocumentAsync(int userId, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return ServiceResult<UploadDocumentResponseDTO>.Failure(
                    "No file uploaded.",
                    "EMPTY_FILE");
            }


            var extension = Path.GetExtension(file.FileName)
                .ToLowerInvariant();


            if (extension != DocumentConstants.PdfExtension)
            {
                return ServiceResult<UploadDocumentResponseDTO>.Failure(
                    "Only PDF files are allowed.",
                    "INVALID_FILE_TYPE");
            }


            if (file.ContentType != DocumentConstants.PdfMimeType)
            {
                return ServiceResult<UploadDocumentResponseDTO>.Failure(
                    "Invalid file format.",
                    "INVALID_MIME_TYPE");
            }


            if (file.Length > DocumentConstants.MaxFileSize)
            {
                return ServiceResult<UploadDocumentResponseDTO>.Failure(
                    "File size exceeds the 25MB limit.",
                    "FILE_TOO_LARGE");
            }


            // Generate file hash
            var fileHash = await _fileHashService.ComputeSha256Async(file);


            // Check duplicate upload
            var existingDocument = await _context.Documents
                .AnyAsync(d =>
                    d.UploadedBy == userId &&
                    d.FileHash == fileHash);


            if (existingDocument)
            {
                return ServiceResult<UploadDocumentResponseDTO>.Failure(
                    "You have already uploaded this document.",
                    "DOCUMENT_ALREADY_EXISTS");
            }


            // Save physical file
            var filePath = await _fileStorageService.SaveFileAsync(file, userId);


            var document = new Document
            {
                Id = Guid.NewGuid(),
                UploadedBy = userId,
                FileName = file.FileName,
                MimeType = file.ContentType,
                FilePath = filePath,
                FileSize = file.Length,
                FileHash = fileHash,
                UploadedAt = DateTime.UtcNow,
                Status = DocumentStatus.Pending
            };


            _context.Documents.Add(document);

            await _context.SaveChangesAsync();


            var response = new UploadDocumentResponseDTO
            {
                Id = document.Id,
                FileName = document.FileName,
                Status = document.Status,
                UploadedAt = document.UploadedAt
            };


            return ServiceResult<UploadDocumentResponseDTO>.Success(response);
        }

        public async Task<ServiceResult<IEnumerable<DocumentListDTO>>> GetUserDocumentsAsync(int userId)
        {
            var documents = await _context.Documents
                .Where(d => d.UploadedBy == userId)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new DocumentListDTO
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    MimeType = d.MimeType,
                    FileSize = d.FileSize,
                    Status = d.Status,
                    UploadedAt = d.UploadedAt
                })
                .ToListAsync();


            return ServiceResult<IEnumerable<DocumentListDTO>>
                .Success(documents);
        }

        public async Task<ServiceResult<DocumentDetailsDTO>> GetDocumentByIdAsync(int userId, Guid documentId)
        {
            var document = await _context.Documents
                .Where(d => d.Id == documentId &&
                            d.UploadedBy == userId)
                .Select(d => new DocumentDetailsDTO
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    MimeType = d.MimeType,
                    FileSize = d.FileSize,
                    Status = d.Status,
                    UploadedAt = d.UploadedAt
                })
                .FirstOrDefaultAsync();


            if (document == null)
            {
                return ServiceResult<DocumentDetailsDTO>.Failure(
                    "Document not found.",
                    "DOCUMENT_NOT_FOUND");
            }


            return ServiceResult<DocumentDetailsDTO>.Success(document);
        }

        public async Task<ServiceResult<bool>> DeleteDocumentAsync(int userId, Guid documentId)
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d =>
                    d.Id == documentId &&
                    d.UploadedBy == userId);


            if (document == null)
            {
                return ServiceResult<bool>.Failure(
                    "Document not found.",
                    "DOCUMENT_NOT_FOUND");
            }


            // Delete physical file
            await _fileStorageService.DeleteFileAsync(
                document.FilePath);


            // Delete database record
            _context.Documents.Remove(document);

            await _context.SaveChangesAsync();


            return ServiceResult<bool>.Success(true);
        }

        public async Task<ServiceResult<Document>> GetDocumentForDownloadAsync(int userId, Guid documentId)
        {
            var document = await _context.Documents
                .FirstOrDefaultAsync(d =>
                    d.Id == documentId &&
                    d.UploadedBy == userId);


            if (document == null)
            {
                return ServiceResult<Document>.Failure(
                    "Document not found.",
                    "DOCUMENT_NOT_FOUND");
            }


            return ServiceResult<Document>.Success(document);
        }

        public async Task<ServiceResult<DocumentStatus>> RetryDocumentUploadAsync(
    int userId,
    Guid documentId,
    CancellationToken cancellationToken = default)
        {
            var document = await _context.Documents
                .AsNoTracking()
                .Where(d =>
                    d.Id == documentId &&
                    d.UploadedBy == userId)
                .Select(d => new
                {
                    d.Status,
                    d.ProcessingLeaseExpiresAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null)
            {
                return ServiceResult<DocumentStatus>.Failure(
                    "Document not found.",
                    "DOCUMENT_NOT_FOUND");
            }

            if (document.Status == DocumentStatus.Indexed)
            {
                return ServiceResult<DocumentStatus>.Failure(
                    "This document is already indexed.",
                    "DOCUMENT_ALREADY_INDEXED");
            }

            if (document.Status == DocumentStatus.Pending)
            {
                return ServiceResult<DocumentStatus>.Success(
                    DocumentStatus.Pending);
            }

            if ((document.Status == DocumentStatus.Processing ||
                 document.Status == DocumentStatus.Chunked) &&
                document.ProcessingLeaseExpiresAt > DateTime.UtcNow)
            {
                return ServiceResult<DocumentStatus>.Failure(
                    "This document is still being processed. Please try again later.",
                    "DOCUMENT_PROCESSING_ACTIVE");
            }

            var queued = await _processingLeaseService.TryQueueRetryAsync(
                documentId,
                userId,
                cancellationToken);

            if (!queued)
            {
                return ServiceResult<DocumentStatus>.Failure(
                    "The document state changed before it could be retried. Refresh the document status and try again.",
                    "DOCUMENT_RETRY_CONFLICT");
            }

            return ServiceResult<DocumentStatus>.Success(
                DocumentStatus.Pending);
        }
    }
}
