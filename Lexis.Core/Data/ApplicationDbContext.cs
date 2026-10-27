using Lexis.Core.Enums;
using Lexis.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lexis.API.Data;

public partial class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public virtual DbSet<Conversation> Conversations { get; set; }

    public virtual DbSet<ConversationDocumentFilter> ConversationDocumentFilters { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<DocumentChunk> DocumentChunks { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<MessageCitation> MessageCitations { get; set; }

    public virtual DbSet<UsageLog> UsageLogs { get; set; }

    public DbSet<RefreshToken> RefreshTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresEnum<DocumentStatus>(
            "document_status");

        modelBuilder.HasPostgresEnum<MessageRole>(
            "message_role");

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FirstName)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(e => e.LastName)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(e => e.CreatedAt)
                  .HasDefaultValueSql("CURRENT_TIMESTAMP")
                  .IsRequired();

            entity.Property(e => e.LastLoginAt);

            entity.Property(e => e.IsActive)
                  .HasDefaultValue(true)
                  .IsRequired();

            entity.Property(e => e.MonthlyTokenAllowance)
                  .HasDefaultValue(100000)
                  .IsRequired();

            entity.Property(e => e.TokensConsumedCurrentMonth)
                  .HasDefaultValue(0)
                  .IsRequired();

            entity.Property(e => e.LastRequestTimestamp);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Token)
                .IsRequired()
                .HasMaxLength(500);
            
            entity.HasIndex(x => x.Token)
                .IsUnique();

            entity.HasIndex(x => new
            {
                x.UserId,
                x.RevokedAt
            });

            entity.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Conversations_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<ConversationDocumentFilter>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ConversationDocumentFilters_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.AppliedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Documents_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.FileHash).IsFixedLength();
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.Status)
                  .HasColumnType("document_status");
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("DocumentChunks_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.ChunkType).HasConversion<int>();
             
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("Messages_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
             
        });

        modelBuilder.Entity<MessageCitation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("MessageCitations_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
             
        });

        modelBuilder.Entity<UsageLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("UsageLogs_pkey");

            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.RequestTime).HasDefaultValueSql("CURRENT_TIMESTAMP");
             
        });

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.User)
            .WithMany(u => u.Conversations)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Document>()
            .HasOne(d => d.UploadedByUser)
            .WithMany(u => u.Documents)
            .HasForeignKey(d => d.UploadedBy)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<UsageLog>()
            .HasOne(u => u.User)
            .WithMany(user => user.UsageLogs)
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentChunk>()
            .HasOne(dc => dc.Document)
            .WithMany(d => d.DocumentChunks)
            .HasForeignKey(dc => dc.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MessageCitation>()
            .HasOne(mc => mc.Message)
            .WithMany(m => m.MessageCitations)
            .HasForeignKey(mc => mc.MessageId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<MessageCitation>()
            .HasOne(mc => mc.DocumentChunk)
            .WithMany(dc => dc.MessageCitations)
            .HasForeignKey(mc => mc.DocumentChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConversationDocumentFilter>()
            .HasOne(cdf => cdf.Conversation)
            .WithMany(c => c.ConversationDocumentFilters)
            .HasForeignKey(cdf => cdf.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<ConversationDocumentFilter>()
            .HasOne(cdf => cdf.Document)
            .WithMany(d => d.ConversationDocumentFilters)
            .HasForeignKey(cdf => cdf.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);


        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
