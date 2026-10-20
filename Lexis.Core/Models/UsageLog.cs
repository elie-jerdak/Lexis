using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lexis.Core.Models;

[Index("UserId", "RequestTime",
    Name = "IX_UsageLogs_UserId_RequestTime")]
public partial class UsageLog
{
    [Key]
    public Guid Id { get; set; }

    public int UserId { get; set; }

    public Guid? MessageId { get; set; }

    public DateTime RequestTime { get; set; }

    public int ResponseTimeMs { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public bool CacheHit { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual ApplicationUser User { get; set; } = null!;

    [ForeignKey("MessageId")]
    [InverseProperty("UsageLogs")]
    public virtual Message? Message { get; set; }
}
