using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;
using Whisper.net;

namespace PhotoService.Services;

/// <summary>
/// Voice prompt moderation pipeline:
///   1. Convert audio to 16kHz PCM WAV (Whisper requires this)
///   2. Transcribe with Whisper.net (local, no API costs)
///   3. Scan transcript for policy violations (hate speech, contact info, etc.)
///   4. Update entity with transcript + moderation status
///
/// Runs async — voice prompt is already live (AUTO_APPROVED) while moderation
/// processes in background. If content is bad, status moves to REJECTED and
/// the existing ServeAudioForUser() query filters it out automatically.
/// </summary>
public partial class VoicePromptModerationService : IVoicePromptModerationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoicePromptModerationService> _logger;
    private readonly IConfiguration _configuration;

    // Lazy-loaded Whisper factory — model is ~150MB, only load once
    private WhisperFactory? _whisperFactory;
    private readonly SemaphoreSlim _factoryLock = new(1, 1);

    public VoicePromptModerationService(
        IServiceProvider serviceProvider,
        ILogger<VoicePromptModerationService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<VoicePromptModerationResult> ModerateAsync(
        int voicePromptId, CancellationToken cancellationToken = default)
    {
        var result = new VoicePromptModerationResult();

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PhotoContext>();

            var voicePrompt = await context.VoicePrompts.FindAsync(new object[] { voicePromptId }, cancellationToken);
            if (voicePrompt == null || voicePrompt.IsDeleted)
            {
                result.ErrorMessage = $"Voice prompt {voicePromptId} not found or deleted";
                return result;
            }

            var filePath = Path.Combine("uploads", "voice-prompts",
                voicePrompt.UserId.ToString(), voicePrompt.StoredFileName);

            if (!File.Exists(filePath))
            {
                result.ErrorMessage = $"Audio file not found: {filePath}";
                _logger.LogWarning("Voice prompt {Id} file missing at {Path}", voicePromptId, filePath);
                return result;
            }

            // Step 1: Transcribe audio
            _logger.LogInformation("Transcribing voice prompt {Id} for user {UserId}",
                voicePromptId, voicePrompt.UserId);

            string transcript;
            try
            {
                transcript = await TranscribeAudioAsync(filePath, cancellationToken);
            }
            catch (Exception ex)
            {
                // Transcription failure is not fatal — log and approve
                _logger.LogWarning(ex, "Whisper transcription failed for voice prompt {Id}; auto-approving", voicePromptId);
                voicePrompt.ModerationStatus = ModerationStatus.Approved;
                voicePrompt.TranscriptText = "[transcription-failed]";
                await context.SaveChangesAsync(cancellationToken);

                result.Success = true;
                result.FinalStatus = ModerationStatus.Approved;
                result.TranscriptText = "[transcription-failed]";
                return result;
            }

            result.TranscriptText = transcript;
            voicePrompt.TranscriptText = transcript;

            // Step 2: Check for policy violations
            var violations = CheckForViolations(transcript);
            result.Violations = violations;

            if (violations.Count > 0)
            {
                voicePrompt.ModerationStatus = ModerationStatus.Rejected;
                result.FinalStatus = ModerationStatus.Rejected;
                _logger.LogWarning("Voice prompt {Id} REJECTED for user {UserId}: {Violations}",
                    voicePromptId, voicePrompt.UserId, string.Join("; ", violations));
            }
            else
            {
                voicePrompt.ModerationStatus = ModerationStatus.Approved;
                result.FinalStatus = ModerationStatus.Approved;
                _logger.LogInformation("Voice prompt {Id} APPROVED for user {UserId}",
                    voicePromptId, voicePrompt.UserId);
            }

            await context.SaveChangesAsync(cancellationToken);
            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moderating voice prompt {Id}", voicePromptId);
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    // ─────────────────── WHISPER TRANSCRIPTION ───────────────────

    private async Task<string> TranscribeAudioAsync(string audioFilePath, CancellationToken ct)
    {
        var factory = await GetOrCreateFactoryAsync(ct);

        using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        // Whisper requires 16kHz mono PCM — we convert from m4a using NAudio-style
        // For MVP, use ffmpeg if available, otherwise process raw.
        var wavPath = await ConvertToWavAsync(audioFilePath, ct);

        try
        {
            await using var fileStream = File.OpenRead(wavPath);
            var segments = new List<string>();

            await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    segments.Add(segment.Text.Trim());
            }

            return string.Join(" ", segments);
        }
        finally
        {
            // Clean up temp WAV
            if (wavPath != audioFilePath && File.Exists(wavPath))
                File.Delete(wavPath);
        }
    }

    private async Task<WhisperFactory> GetOrCreateFactoryAsync(CancellationToken ct)
    {
        if (_whisperFactory != null) return _whisperFactory;

        await _factoryLock.WaitAsync(ct);
        try
        {
            if (_whisperFactory != null) return _whisperFactory;

            var modelPath = _configuration["VoiceModeration:WhisperModelPath"]
                ?? Path.Combine("models", "ggml-base.bin");

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation("Downloading Whisper base model to {Path}...", modelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

                // Download ggml-base.bin (~142MB) from Hugging Face
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
                var modelUrl = "https://huggingface.co/sandrohanea/whisper.net/resolve/v3/classic/ggml-base.bin";
                using var response = await httpClient.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var downloadStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = File.Create(modelPath);
                await downloadStream.CopyToAsync(fileStream, ct);
                _logger.LogInformation("Whisper model downloaded ({Size} bytes)", new FileInfo(modelPath).Length);
            }

            _whisperFactory = WhisperFactory.FromPath(modelPath);
            return _whisperFactory;
        }
        finally
        {
            _factoryLock.Release();
        }
    }

    /// <summary>
    /// Convert m4a/aac to 16kHz mono WAV using ffmpeg (available in Docker image).
    /// Falls back to original file if ffmpeg is not found (will fail in Whisper but logged).
    /// </summary>
    private async Task<string> ConvertToWavAsync(string inputPath, CancellationToken ct)
    {
        var wavPath = Path.ChangeExtension(inputPath, ".moderation.wav");

        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{inputPath}\" -ar 16000 -ac 1 -f wav \"{wavPath}\" -y",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            await process.WaitForExitAsync(ct);

            if (process.ExitCode == 0 && File.Exists(wavPath))
                return wavPath;

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogWarning("ffmpeg conversion failed (exit {Code}): {Stderr}", process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg not available, attempting direct Whisper processing");
        }

        return inputPath; // fallback — let Whisper try the raw file
    }

    // ─────────────────── TEXT MODERATION ───────────────────

    /// <summary>
    /// Check transcript for policy violations.
    /// Categories: hate speech, slurs, contact information, sexual content.
    /// </summary>
    internal static List<string> CheckForViolations(string transcript)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(transcript)) return violations;

        var lower = transcript.ToLowerInvariant();

        // Category 1: Phone numbers (7+ digits, with optional separators)
        if (PhonePattern().IsMatch(lower))
            violations.Add("contact_info:phone_number");

        // Category 2: Email addresses
        if (EmailPattern().IsMatch(lower))
            violations.Add("contact_info:email");

        // Category 3: Social media handles (@username, instagram, snapchat, etc.)
        if (SocialMediaPattern().IsMatch(lower))
            violations.Add("contact_info:social_media");

        // Category 4: Hate speech / slurs — word-boundary matched
        foreach (var word in HateSpeechWords)
        {
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(word)}\b"))
            {
                violations.Add($"hate_speech:{word}");
                break; // One match is enough
            }
        }

        // Category 5: Explicit sexual content
        foreach (var word in ExplicitContentWords)
        {
            if (Regex.IsMatch(lower, $@"\b{Regex.Escape(word)}\b"))
            {
                violations.Add($"explicit_content:{word}");
                break;
            }
        }

        return violations;
    }

    // ─── Regex patterns (source-generated for performance) ───

    [GeneratedRegex(@"(\+?\d[\d\s\-\.]{6,}\d)", RegexOptions.Compiled)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.Compiled)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(instagram|snapchat|snap|insta|tiktok|twitter|whatsapp|telegram|@\w{3,})", RegexOptions.Compiled)]
    private static partial Regex SocialMediaPattern();

    // Blocklist — intentionally short for MVP. Real production uses ML classifier.
    private static readonly HashSet<string> HateSpeechWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "kill", "murder", "terrorist", "bomb", "shoot",
    };

    private static readonly HashSet<string> ExplicitContentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "porn", "xxx", "nude", "naked",
    };
}
