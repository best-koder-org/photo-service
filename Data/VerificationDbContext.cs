using Microsoft.EntityFrameworkCore;
using PhotoService.Models;

namespace PhotoService.Data;

/// <summary>
/// Separate DbContext for verification tracking.
/// Shares the same database but owns only the VerificationAttempts table.
/// Register in DI: builder.Services.AddDbContext<VerificationDbContext>(...)
/// </summary>
public class VerificationDbContext : DbContext
{
    public VerificationDbContext(DbContextOptions<VerificationDbContext> options) : base(options)
    {
    }

    public DbSet<VerificationAttempt> VerificationAttempts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VerificationAttempt>(entity =>
        {
            entity.ToTable("verification_attempts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.SimilarityScore);
            entity.Property(e => e.ProfilePhotoId);
            entity.Property(e => e.Result).HasMaxLength(50);
            entity.Property(e => e.RejectionReason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
        });
    }
}
