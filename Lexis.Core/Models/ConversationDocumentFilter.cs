using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Models;

[Index("ConversationId", "DocumentId", Name = "UX_Conversation_Document", IsUnique = true)]
public partial class ConversationDocumentFilter
{
    [Key]
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid DocumentId { get; set; }

    public bool IsActive { get; set; }

    public DateTime AppliedAt { get; set; }

    [ForeignKey("ConversationId")]
    [InverseProperty("ConversationDocumentFilters")]
    public virtual Conversation Conversation { get; set; } = null!;

    [ForeignKey("DocumentId")]
    [InverseProperty("ConversationDocumentFilters")]
    public virtual Document Document { get; set; } = null!;
}
