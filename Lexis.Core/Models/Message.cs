using Lexis.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lexis.Core.Models;

[Index("ConversationId", "CreatedAt", Name = "IX_Messages_ConversationId_CreatedAt")]
public partial class Message
{
    [Key]
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Content { get; set; } = null!;

    public MessageRole Role { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public bool IsError { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey("ConversationId")]
    [InverseProperty("Messages")]
    public virtual Conversation Conversation { get; set; } = null!;

    [InverseProperty("Message")]
    public virtual ICollection<MessageCitation> MessageCitations { get; set; } = new List<MessageCitation>();

    [InverseProperty("Message")]
    public virtual ICollection<UsageLog> UsageLogs { get; set; } = new List<UsageLog>();
}
