using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PhotoService.Models;

/// <summary>
/// Voice prompt entity — one prompt per user, stored as AAC audio.
/// Follows the same moderation pattern as Photo entity.
/// </summary>
[Table("voice_prompts")]
public class VoicePrompt
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>User profile ID (from UserService)</summary>
    [Required]
    public int UserId { get; set; }

    /// <summary>Stored filename: {userId}_{timestamp}_{guid}.m4a</summary>
    [Required]
    [MaxLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    /// <summary>File size in bytes</summary>
    [Required]
    public long FileSizeBytes { get; set; }

    /// <summary>Duration in seconds (server-validated)</summary>
    [Required]
    public double DurationSeconds { get; set; }

    /// <summary>MIME type — always audio/mp4 for AAC</summary>
    [Required]
    [MaxLength(50)]
    public string MimeType { get; set; } = "audio/mp4";

    /// <summary>Content moderation status</summary>
    [Required]
    [MaxLength(20)]
    public string ModerationStatus { get; set; } = "AUTO_APPROVED";

    /// <summary>Speech-to-text transcript for moderation review</summary>
    [MaxLength(2000)]
    public string? TranscriptText { get; set; }

    /// <summary>SHA-256 content hash for duplicate detection</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Required]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    /// <summary>Calculated file path for storage</summary>
    [NotMapped]
    public string FilePath => $"uploads/voice-prompts/{UserId}/{StoredFileName}";

    /// <summary>Public URL for audio access</summary>
    [NotMapped]
    public string Url => $"/api/voice-prompts/audio";
}

/// <summary>Voice prompt constants</summary>
public static class VoicePromptConstants
{
    public const int MinDurationSeconds = 3;
    public const int MaxDurationSeconds = 30;
    public const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB
    public static readonly string[] AllowedMimeTypes = { "audio/mp4", "audio/aac", "audio/m4a", "audio/mpeg" };
}
