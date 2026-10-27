namespace Lexis.Core.DTOs.DocumentProcessing
{
    public sealed class ClaimedDocument
    {
        public Guid DocumentId { get; set; }

        public int UploadedBy { get; set; }

        public string FileName { get; set; } = string.Empty;

        public Guid LeaseId { get; set; }

        public int ProcessingAttempt { get; set; }
    }
}
