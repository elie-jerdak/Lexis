using Lexis.Core.Enums;

namespace Lexis.Core.DTOs.Document
{
    public class UploadDocumentResponseDTO
    {
        public Guid Id { get; set; }

        public string FileName { get; set; } = null!;

        public DocumentStatus Status { get; set; }

        public DateTime UploadedAt { get; set; }
    }
}
