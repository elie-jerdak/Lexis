using Lexis.Core.DTOs.Document;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.Interfaces
{
    public interface IDocumentService
    {
        Task<ServiceResult<UploadDocumentResponseDTO>> UploadDocumentAsync(int userId, IFormFile file);
        Task<ServiceResult<IEnumerable<DocumentListDTO>>> GetUserDocumentsAsync(int userId);
        Task<ServiceResult<DocumentDetailsDTO>> GetDocumentByIdAsync(int userId, Guid documentId);
        Task<ServiceResult<bool>> DeleteDocumentAsync(int userId, Guid documentId);
        Task<ServiceResult<Document>> GetDocumentForDownloadAsync(int userId, Guid documentId);
        Task<ServiceResult<DocumentStatus>> RetryDocumentUploadAsync(int userId, Guid documentId, 
            CancellationToken cancellationToken = default);
    }
}
