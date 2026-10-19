using Lexis.Core.DTOs.MessageCitation;
using Lexis.Core.Enums;

namespace Lexis.Core.DTOs.Conversation
{
    public sealed class ConversationMessageDTO
    {
        public Guid Id { get; set; }
        public string Role { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<MessageCitationDTO> Citations { get; set; } = new();
    }
}
