using Lexis.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lexis.API.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
            
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(entity =>
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
            });
        }
    }
}
