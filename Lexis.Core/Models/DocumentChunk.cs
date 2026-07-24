using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Lexis.Core.Models;

[Index("DocumentId", "ChunkIndex", Name = "UX_DocumentChunks_Document_Chunk", IsUnique = true)]
[Index("QdrantVectorId", Name = "UX_DocumentChunks_QdrantVectorId", IsUnique = true)]
public partial class DocumentChunk
{
    [Key]
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string Content { get; set; } = null!;

    public int? PageNumber { get; set; }

    public int ChunkIndex { get; set; }

    public int TokenCount { get; set; }

    [StringLength(64)]
    public string QdrantVectorId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    [ForeignKey("DocumentId")]
    [InverseProperty("DocumentChunks")]
    public virtual Document Document { get; set; } = null!;

    [InverseProperty("DocumentChunk")]
    public virtual ICollection<MessageCitation> MessageCitations { get; set; } = new List<MessageCitation>();
}
