using Lexis.Core.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Lexis.Core.DTOs.Document
{
    public class DocumentDetailsDTO
    {
        public Guid Id { get; set; }

        public string FileName { get; set; } = null!;

        public string MimeType { get; set; } = null!;

        public long FileSize { get; set; }

        public DocumentStatus Status { get; set; }

        public DateTime UploadedAt { get; set; }
    }
}
