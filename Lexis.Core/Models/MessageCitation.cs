using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Linq;

namespace Lexis.Core.Models;

[Index("MessageId", Name = "IX_MessageCitations_MessageId")]
[Index("DocumentChunkId", Name = "IX_MessageCitations_DocumentChunkId")]
public partial class MessageCitation
{
    [Key]
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    public Guid DocumentChunkId { get; set; }

    public float SimilarityScore { get; set; }

    public string? TextContextSnapshot { get; set; }

    public DateTime CreatedAt { get; set; }

    [ForeignKey("DocumentChunkId")]
    [InverseProperty("MessageCitations")]
    public virtual DocumentChunk DocumentChunk { get; set; } = null!;

    [ForeignKey("MessageId")]
    [InverseProperty("MessageCitations")]
    public virtual Message Message { get; set; } = null!;
}
