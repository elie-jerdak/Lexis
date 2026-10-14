using System.ComponentModel.DataAnnotations;

namespace Lexis.Core.DTOs.Conversation
{
    public sealed class UpdateConversationDocumentsRequestDTO
    {
        [Required]
        public List<Guid> DocumentIds { get; set; } = new();
    }
}
