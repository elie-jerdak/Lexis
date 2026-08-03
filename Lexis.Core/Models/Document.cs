using Lexis.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lexis.Core.Models;

[Index("UploadedBy", "UploadedAt", Name = "IX_Documents_UploadedBy_UploadedAt", IsDescending = new[] { false, true })]
[Index("UploadedBy", "FileHash", Name = "UX_Documents_User_FileHash", IsUnique = true)]
public partial class Document
{
    [Key]
    public Guid Id { get; set; }

    public int UploadedBy { get; set; }

    public DocumentStatus Status { get; set; }

    [StringLength(255)]
    public string FileName { get; set; } = null!;

    [StringLength(100)]
    public string MimeType { get; set; } = null!;

    [StringLength(512)]
    public string FilePath { get; set; } = null!;

    public long FileSize { get; set; }

    [StringLength(64)]
    public string FileHash { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public DateTime? ProcessingStartedAt { get; set; }

    public DateTime? ProcessingCompletedAt { get; set; }

    public string? ProcessingError { get; set; }

    [ForeignKey(nameof(UploadedBy))]
    public virtual ApplicationUser UploadedByUser { get; set; } = null!;

    [InverseProperty("Document")]
    public virtual ICollection<ConversationDocumentFilter> ConversationDocumentFilters { get; set; } = new List<ConversationDocumentFilter>();

    [InverseProperty("Document")]
    public virtual ICollection<DocumentChunk> DocumentChunks { get; set; } = new List<DocumentChunk>();
}
