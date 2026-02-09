using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PhotoService.Services;

namespace PhotoService.Models;

/// <summary>
/// Tracks face verification attempts for self-hosted DeepFace badge system.
/// </summary>
[Table("verification_attempts")]
public class VerificationAttempt
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    public double SimilarityScore { get; set; }

    public int ProfilePhotoId { get; set; }

    [MaxLength(50)]
    public string Result { get; set; } = "";

    public VerificationDecision Decision { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    public bool AntiSpoofingPassed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
