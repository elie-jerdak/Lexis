using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Models;

[Index("UserId", "IsDeleted", "UpdatedAt", Name = "IX_Conversations_UserId_IsDeleted_UpdatedAt", IsDescending = new[] { false, false, true })]
public partial class Conversation
{
    [Key]
    public Guid Id { get; set; }

    public int UserId { get; set; }

    [StringLength(255)]
    public string Title { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [InverseProperty("Conversation")]
    public virtual ICollection<ConversationDocumentFilter> ConversationDocumentFilters { get; set; } = new List<ConversationDocumentFilter>();

    [InverseProperty("Conversation")]
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
