namespace PhotoService.Services;

/// <summary>
/// Interface for voice prompt audio moderation.
/// Transcribes audio via Whisper.net and scans text for policy violations.
/// </summary>
public interface IVoicePromptModerationService
{
    /// <summary>
    /// Moderate a voice prompt audio file.
    /// Transcribes to text, checks for forbidden content, and updates the entity.
    /// </summary>
    /// <param name="voicePromptId">Voice prompt entity ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Moderation result</returns>
    Task<VoicePromptModerationResult> ModerateAsync(int voicePromptId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from voice prompt moderation pipeline.
/// </summary>
public class VoicePromptModerationResult
{
    public bool Success { get; set; }
    public string? TranscriptText { get; set; }
    public string FinalStatus { get; set; } = string.Empty;
    public List<string> Violations { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
