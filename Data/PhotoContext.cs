using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PhotoService.Models;
using System.Text.Json;

namespace PhotoService.Data;

/// <summary>
/// MySQL-optimized Entity Framework context for photo management
/// Designed for MySQL 8.0 with modern .NET 8 patterns
/// </summary>
public class PhotoContext : DbContext
{
    public PhotoContext(DbContextOptions<PhotoContext> options) : base(options)
    {
    }

    // Main entities
    public DbSet<Photo> Photos { get; set; }
    public DbSet<PhotoProcessingJob> PhotoProcessingJobs { get; set; }
    public DbSet<PhotoModerationLog> PhotoModerationLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure MySQL-specific settings
        ConfigurePhotoEntity(modelBuilder);
        ConfigurePhotoProcessingJobEntity(modelBuilder);
        ConfigurePhotoModerationLogEntity(modelBuilder);
    }

    // Shared JSON converter for JsonDocument properties
    private static readonly ValueConverter<JsonDocument?, string?> JsonDocumentToStringConverter =
        new ValueConverter<JsonDocument?, string?>(
            v => v == null ? null : v.RootElement.GetRawText(),
            v => v == null ? null : JsonDocument.Parse(v, default));

    private void ConfigurePhotoEntity(ModelBuilder modelBuilder)
    {
        var photoEntity = modelBuilder.Entity<Photo>();

        // Table configuration
        photoEntity.ToTable("photos"); // PostgreSQL convention: lowercase with underscores

        // Primary key
        photoEntity.HasKey(p => p.Id);
        photoEntity.Property(p => p.Id)
            .HasColumnName("id"); // MySQL auto-increment for primary keys

        // User reference
        photoEntity.Property(p => p.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        // File information
        photoEntity.Property(p => p.OriginalFileName)
            .HasColumnName("original_file_name")
            .HasMaxLength(255)
            .IsRequired();

        photoEntity.Property(p => p.StoredFileName)
            .HasColumnName("stored_file_name")
            .HasMaxLength(255)
            .IsRequired();

        photoEntity.Property(p => p.FileExtension)
            .HasColumnName("file_extension")
            .HasMaxLength(10)
            .IsRequired();

        photoEntity.Property(p => p.FileSizeBytes)
            .HasColumnName("file_size_bytes")
            .IsRequired();

        photoEntity.Property(p => p.MimeType)
            .HasColumnName("mime_type")
            .HasMaxLength(100)
            .IsRequired();

        // Image dimensions
        photoEntity.Property(p => p.Width)
            .HasColumnName("width")
            .IsRequired();

        photoEntity.Property(p => p.Height)
            .HasColumnName("height")
            .IsRequired();

        // Display and ordering
        photoEntity.Property(p => p.DisplayOrder)
            .HasColumnName("display_order")
            .HasDefaultValue(1)
            .IsRequired();

        photoEntity.Property(p => p.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .IsRequired();

        // Timestamps - PostgreSQL-optimized
        photoEntity.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        photoEntity.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        // Soft delete
        photoEntity.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        photoEntity.Property(p => p.DeletedAt)
            .HasColumnName("deleted_at")
            .HasColumnType("timestamp with time zone");

        // Moderation
        photoEntity.Property(p => p.ModerationStatus)
            .HasColumnName("moderation_status")
            .HasMaxLength(20)
            .HasDefaultValue("AUTO_APPROVED")
            .IsRequired();

        photoEntity.Property(p => p.ModerationNotes)
            .HasColumnName("moderation_notes")
            .HasMaxLength(1000);

        photoEntity.Property(p => p.QualityScore)
            .HasColumnName("quality_score")
            .HasDefaultValue(100)
            .IsRequired();

        // MySQL JSON column for flexible metadata
        photoEntity.Property(p => p.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("json")
            .HasConversion(JsonDocumentToStringConverter);

        // Moderation results as JSON
        photoEntity.Property(p => p.ModerationResults)
            .HasColumnName("moderation_results")
            .HasColumnType("json")
            .HasConversion(JsonDocumentToStringConverter);

        // Tags as JSON array (MySQL doesn't support native arrays)
        photoEntity.Property(p => p.Tags)
            .HasColumnName("tags")
            .HasColumnType("json"); // MySQL JSON array

        // Hash for duplicate detection
        photoEntity.Property(p => p.ContentHash)
            .HasColumnName("content_hash")
            .HasMaxLength(64);

        // Indexes for performance
        photoEntity.HasIndex(p => p.UserId)
            .HasDatabaseName("ix_photos_user_id");

        photoEntity.HasIndex(p => new { p.UserId, p.IsDeleted, p.DisplayOrder })
            .HasDatabaseName("ix_photos_user_active_display_order");

        photoEntity.HasIndex(p => new { p.UserId, p.IsPrimary, p.IsDeleted })
            .HasDatabaseName("ix_photos_user_primary_active");

        photoEntity.HasIndex(p => p.ContentHash)
            .HasDatabaseName("ix_photos_content_hash");

        photoEntity.HasIndex(p => p.ModerationStatus)
            .HasDatabaseName("ix_photos_moderation_status");

        // T062: Additional composite indexes for query optimization  
        photoEntity.HasIndex(p => new { p.UserId, p.IsDeleted, p.IsPrimary, p.DisplayOrder })
            .HasDatabaseName("ix_photos_user_ordering");

        photoEntity.HasIndex(p => new { p.ModerationStatus, p.IsDeleted, p.CreatedAt })
            .HasDatabaseName("ix_photos_moderation_queue");

        // Index for JSONB metadata (standard index for MySQL)
        photoEntity.HasIndex(p => p.Metadata)
            .HasDatabaseName("ix_photos_metadata");

        // Index for tags (standard index for MySQL)
        photoEntity.HasIndex(p => p.Tags)
            .HasDatabaseName("ix_photos_tags");

        // Constraints
        photoEntity.HasCheckConstraint("ck_photos_quality_score_range",
            "quality_score >= 0 AND quality_score <= 100");
        photoEntity.HasCheckConstraint("ck_photos_display_order_positive",
            "display_order > 0");
        photoEntity.HasCheckConstraint("ck_photos_dimensions_positive",
            "width > 0 AND height > 0");
        photoEntity.HasCheckConstraint("ck_photos_file_size_positive",
            "file_size_bytes > 0");
    }

    private void ConfigurePhotoProcessingJobEntity(ModelBuilder modelBuilder)
    {
        var jobEntity = modelBuilder.Entity<PhotoProcessingJob>();

        jobEntity.ToTable("photo_processing_jobs");

        jobEntity.HasKey(j => j.Id);
        jobEntity.Property(j => j.Id)
            .HasColumnName("id"); // MySQL auto-increment

        jobEntity.Property(j => j.PhotoId)
            .HasColumnName("photo_id")
            .IsRequired();

        jobEntity.Property(j => j.JobType)
            .HasColumnName("job_type")
            .HasMaxLength(50)
            .IsRequired();

        jobEntity.Property(j => j.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("PENDING")
            .IsRequired();

        jobEntity.Property(j => j.Parameters)
            .HasColumnName("parameters")
            .HasColumnType("json")
            .HasConversion(JsonDocumentToStringConverter);

        jobEntity.Property(j => j.Result)
            .HasColumnName("result")
            .HasColumnType("json")
            .HasConversion(JsonDocumentToStringConverter);

        jobEntity.Property(j => j.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(1000);

        jobEntity.Property(j => j.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        jobEntity.Property(j => j.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        jobEntity.Property(j => j.CompletedAt)
            .HasColumnName("completed_at")
            .HasColumnType("timestamp with time zone");

        // Foreign key relationship
        jobEntity.HasOne<Photo>()
            .WithMany()
            .HasForeignKey(j => j.PhotoId)
            .HasConstraintName("fk_photo_processing_jobs_photo_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        jobEntity.HasIndex(j => j.PhotoId)
            .HasDatabaseName("ix_photo_processing_jobs_photo_id");

        jobEntity.HasIndex(j => j.Status)
            .HasDatabaseName("ix_photo_processing_jobs_status");

        jobEntity.HasIndex(j => j.CreatedAt)
            .HasDatabaseName("ix_photo_processing_jobs_created_at");
    }

    private void ConfigurePhotoModerationLogEntity(ModelBuilder modelBuilder)
    {
        var logEntity = modelBuilder.Entity<PhotoModerationLog>();

        logEntity.ToTable("photo_moderation_logs");

        logEntity.HasKey(l => l.Id);
        logEntity.Property(l => l.Id)
            .HasColumnName("id"); // MySQL auto-increment

        logEntity.Property(l => l.PhotoId)
            .HasColumnName("photo_id")
            .IsRequired();

        logEntity.Property(l => l.PreviousStatus)
            .HasColumnName("previous_status")
            .HasMaxLength(20);

        logEntity.Property(l => l.NewStatus)
            .HasColumnName("new_status")
            .HasMaxLength(20)
            .IsRequired();

        logEntity.Property(l => l.ModeratorId)
            .HasColumnName("moderator_id");

        logEntity.Property(l => l.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        logEntity.Property(l => l.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1000);

        logEntity.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Foreign key relationship
        logEntity.HasOne<Photo>()
            .WithMany()
            .HasForeignKey(l => l.PhotoId)
            .HasConstraintName("fk_photo_moderation_logs_photo_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        logEntity.HasIndex(l => l.PhotoId)
            .HasDatabaseName("ix_photo_moderation_logs_photo_id");

        logEntity.HasIndex(l => l.ModeratorId)
            .HasDatabaseName("ix_photo_moderation_logs_moderator_id");

        logEntity.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("ix_photo_moderation_logs_created_at");
    }

    /// <summary>
    /// Override SaveChanges to handle automatic timestamp updates
    /// </summary>
    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to handle automatic timestamp updates
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Automatically update timestamps on entity changes
    /// </summary>
    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries<Photo>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;

                case EntityState.Deleted:
                    // Convert hard delete to soft delete
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }
    }
}
