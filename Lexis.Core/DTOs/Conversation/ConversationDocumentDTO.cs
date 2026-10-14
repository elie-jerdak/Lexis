using Lexis.Core.Enums; 

namespace Lexis.Core.DTOs.Conversation
{
    public sealed class ConversationDocumentDTO
    {
        public Guid Id { get; init; }

        public string FileName { get; init; } = string.Empty;

        public DocumentStatus Status { get; init; }

        public DateTime UploadedAt { get; init; }

        public bool IsActive { get; init; }

        public DateTime AppliedAt { get; init; }
    }
}
