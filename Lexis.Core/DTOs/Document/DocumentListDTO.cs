using Lexis.Core.Enums;

namespace Lexis.Core.DTOs.Document
{
    public class DocumentListDTO
    {
        public Guid Id { get; set; }

        public string FileName { get; set; } = null!;

        public string MimeType { get; set; } = null!;

        public long FileSize { get; set; }

        public DocumentStatus Status { get; set; }

        public DateTime UploadedAt { get; set; }
    }
}
