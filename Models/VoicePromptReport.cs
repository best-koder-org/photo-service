namespace PhotoService.Models;

/// <summary>
/// User report against a voice prompt.
/// When a user flags a voice prompt, the prompt goes to PENDING_REVIEW
/// and a report record is created for the trust & safety team.
/// </summary>
public class VoicePromptReport
{
    public int Id { get; set; }

    /// <summary>Voice prompt being reported</summary>
    public int VoicePromptId { get; set; }

    /// <summary>User who filed the report</summary>
    public int ReporterUserId { get; set; }

    /// <summary>User who owns the reported voice prompt</summary>
    public int TargetUserId { get; set; }

    /// <summary>Reason category: inappropriate, harassment, spam, other</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Optional free-text description</summary>
    public string? Description { get; set; }

    /// <summary>Report status: pending, reviewed, dismissed</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
