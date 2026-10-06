using Lexis.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lexis.Core.Models;

[Index("DocumentId", "ChunkIndex", Name = "UX_DocumentChunks_Document_Chunk", IsUnique = true)]
[Index("QdrantVectorId", Name = "UX_DocumentChunks_QdrantVectorId", IsUnique = true)]
public partial class DocumentChunk
{
    [Key]
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Order of the chunk within the document.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// First page covered by this chunk.
    /// </summary>
    public int StartPage { get; set; }

    /// <summary>
    /// Last page covered by this chunk.
    /// </summary>
    public int EndPage { get; set; }

    /// <summary>
    /// Section heading (or heading path) this chunk belongs to.
    /// Example:
    /// "6 Planning > 6.1 Actions to address risks"
    /// </summary>
    [StringLength(500)]
    public string? Heading { get; set; }
    public string? HeadingPath { get; set; }

    public ChunkType ChunkType { get; set; }
    public int? TableId { get; set; }
    public int? FigureId { get; set; }
    public ContinuationType ContinuationType { get; set; }
    [NotMapped]
    public bool IsPending { get; set; }
    public int TokenCount { get; set; }

    [StringLength(64)]
    public string? QdrantVectorId { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    [ForeignKey("DocumentId")]
    [InverseProperty("DocumentChunks")]
    public virtual Document Document { get; set; } = null!;

    [InverseProperty("DocumentChunk")]
    public virtual ICollection<MessageCitation> MessageCitations { get; set; } = new List<MessageCitation>();
}
