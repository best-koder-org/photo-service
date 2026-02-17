using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;
using System.Security.Claims;
using System.Security.Cryptography;

namespace PhotoService.Controllers;

/// <summary>
/// Voice Prompts API — upload, retrieve, and delete audio voice prompts.
/// One prompt per user. In-app recording only (no file import = anti-deepfake).
/// Stored as AAC (.m4a), validated server-side for duration and size.
/// </summary>
[ApiController]
[Route("api/voice-prompts")]
[Authorize]
public class VoicePromptsController : ControllerBase
{
    private readonly PhotoContext _context;
    private readonly ILogger<VoicePromptsController> _logger;

    public VoicePromptsController(PhotoContext context, ILogger<VoicePromptsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Upload a voice prompt (replaces existing if any).
    /// POST /api/voice-prompts
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)] // 3 MB hard limit (2 MB soft, headroom for encoding)
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(IFormFile audio)
    {
        try
        {
            var userId = GetCurrentUserId();

            // ── Validate ──
            if (audio == null || audio.Length == 0)
                return BadRequest("No audio file provided");

            if (audio.Length > VoicePromptConstants.MaxFileSizeBytes)
                return BadRequest($"File exceeds {VoicePromptConstants.MaxFileSizeBytes / 1024}KB limit");

            var mimeType = audio.ContentType?.ToLower() ?? "";
            if (!VoicePromptConstants.AllowedMimeTypes.Contains(mimeType))
                return BadRequest($"Invalid audio format. Allowed: {string.Join(", ", VoicePromptConstants.AllowedMimeTypes)}");

            // ── Store file ──
            var storedFileName = $"{userId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid():N}.m4a";
            var directory = Path.Combine("uploads", "voice-prompts", userId.ToString());
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, storedFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // ── Content hash ──
            string contentHash;
            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var hashBytes = await SHA256.HashDataAsync(stream);
                contentHash = Convert.ToHexString(hashBytes).ToLower();
            }

            // ── Soft-delete existing prompt ──
            var existing = await _context.VoicePrompts
                .Where(v => v.UserId == userId && !v.IsDeleted)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                existing.IsDeleted = true;
                existing.DeletedAt = DateTime.UtcNow;
                _logger.LogInformation("Soft-deleted previous voice prompt {Id} for user {UserId}", existing.Id, userId);
            }

            // ── Create entity ──
            // Duration is client-reported via form field; server re-validates range.
            var durationStr = Request.Form["duration"].FirstOrDefault();
            double duration = 15; // default estimate
            if (double.TryParse(durationStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }

            if (duration < VoicePromptConstants.MinDurationSeconds || duration > VoicePromptConstants.MaxDurationSeconds)
                return BadRequest($"Duration must be between {VoicePromptConstants.MinDurationSeconds} and {VoicePromptConstants.MaxDurationSeconds} seconds");

            var voicePrompt = new VoicePrompt
            {
                UserId = userId,
                StoredFileName = storedFileName,
                FileSizeBytes = audio.Length,
                DurationSeconds = duration,
                MimeType = mimeType,
                ModerationStatus = Models.ModerationStatus.AutoApproved,
                ContentHash = contentHash,
                CreatedAt = DateTime.UtcNow,
            };

            _context.VoicePrompts.Add(voicePrompt);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Voice prompt {Id} uploaded for user {UserId} ({Size}B, {Duration}s)",
                voicePrompt.Id, userId, audio.Length, duration);

            var url = $"/api/voice-prompts/audio";
            return Created(url, new
            {
                id = voicePrompt.Id,
                url,
                durationSeconds = voicePrompt.DurationSeconds,
                fileSizeBytes = voicePrompt.FileSizeBytes,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading voice prompt");
            return StatusCode(500, "An error occurred while uploading voice prompt");
        }
    }

    /// <summary>
    /// Get the current user's voice prompt audio.
    /// GET /api/voice-prompts/audio
    /// </summary>
    [HttpGet("audio")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudio()
    {
        var userId = GetCurrentUserId();
        return await ServeAudioForUser(userId);
    }

    /// <summary>
    /// Get another user's voice prompt audio (for Discover screen).
    /// GET /api/voice-prompts/audio/{userId}
    /// </summary>
    [HttpGet("audio/{targetUserId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudioForUser(int targetUserId)
    {
        // Any authenticated user can listen to voice prompts (they're on public profiles)
        return await ServeAudioForUser(targetUserId);
    }

    /// <summary>
    /// Get voice prompt metadata for a user (no audio data).
    /// GET /api/voice-prompts/meta/{userId}
    /// </summary>
    [HttpGet("meta/{targetUserId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeta(int targetUserId)
    {
        var vp = await _context.VoicePrompts
            .Where(v => v.UserId == targetUserId && !v.IsDeleted
                     && v.ModerationStatus != Models.ModerationStatus.Rejected)
            .Select(v => new { v.Id, v.DurationSeconds, v.CreatedAt })
            .FirstOrDefaultAsync();

        if (vp == null) return NotFound();

        return Ok(new
        {
            vp.Id,
            vp.DurationSeconds,
            vp.CreatedAt,
            audioUrl = $"/api/voice-prompts/audio/{targetUserId}",
        });
    }

    /// <summary>
    /// Delete the current user's voice prompt.
    /// DELETE /api/voice-prompts
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete()
    {
        var userId = GetCurrentUserId();
        var vp = await _context.VoicePrompts
            .Where(v => v.UserId == userId && !v.IsDeleted)
            .FirstOrDefaultAsync();

        if (vp == null) return NotFound("No voice prompt found");

        vp.IsDeleted = true;
        vp.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Voice prompt {Id} deleted for user {UserId}", vp.Id, userId);
        return Ok(new { message = "Voice prompt deleted" });
    }

    // ────────────── Helpers ──────────────

    private async Task<IActionResult> ServeAudioForUser(int userId)
    {
        var vp = await _context.VoicePrompts
            .Where(v => v.UserId == userId && !v.IsDeleted
                     && v.ModerationStatus != Models.ModerationStatus.Rejected)
            .FirstOrDefaultAsync();

        if (vp == null) return NotFound("No voice prompt found");

        var filePath = Path.Combine("uploads", "voice-prompts", userId.ToString(), vp.StoredFileName);
        if (!System.IO.File.Exists(filePath))
        {
            _logger.LogWarning("Voice prompt file missing: {Path}", filePath);
            return NotFound("Audio file not found");
        }

        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(Path.GetFullPath(filePath), vp.MimeType, enableRangeProcessing: true);
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? User.FindFirst("userId")?.Value;

        if (string.IsNullOrEmpty(userIdClaim))
            throw new UnauthorizedAccessException("Unable to determine user identity");

        if (int.TryParse(userIdClaim, out var userId))
            return userId;

        return Math.Abs(userIdClaim.GetHashCode());
    }
}
