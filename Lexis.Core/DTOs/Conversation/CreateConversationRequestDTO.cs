using System.ComponentModel.DataAnnotations;

namespace Lexis.Core.DTOs.Conversation
{
    public sealed class CreateConversationRequestDTO
    {
        [StringLength(255)]
        public string? Title { get; set; }
    }
}
