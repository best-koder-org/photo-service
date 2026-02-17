using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;
using System.Security.Claims;
using System.Security.Cryptography;

namespace PhotoService.Controllers;

/// <summary>
/// Voice Prompts API — upload, retrieve, delete, and report audio voice prompts.
/// One prompt per user. In-app recording only (no file import = anti-deepfake).
/// Stored as AAC (.m4a), validated server-side for duration and size.
///
/// Moderation flow:
///   Upload → AUTO_APPROVED (served immediately) →
///   Background service transcribes via Whisper.net →
///   Text checked for policy violations →
///   APPROVED or REJECTED (rejected prompts filtered from queries automatically).
///
/// Users can also report voice prompts → PENDING_REVIEW for trust & safety.
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
    /// Goes live immediately as AUTO_APPROVED; background moderation follows.
    /// POST /api/voice-prompts
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
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

            // ── Parse duration ──
            var durationStr = Request.Form["duration"].FirstOrDefault();
            double duration = 15;
            if (double.TryParse(durationStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }

            if (duration < VoicePromptConstants.MinDurationSeconds || duration > VoicePromptConstants.MaxDurationSeconds)
                return BadRequest($"Duration must be between {VoicePromptConstants.MinDurationSeconds} and {VoicePromptConstants.MaxDurationSeconds} seconds");

            // ── Create entity (AUTO_APPROVED — served immediately) ──
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

            _logger.LogInformation(
                "Voice prompt {Id} uploaded for user {UserId} ({Size}B, {Duration}s) — queued for async moderation",
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
    /// Rejected prompts are automatically filtered out.
    /// GET /api/voice-prompts/audio/{userId}
    /// </summary>
    [HttpGet("audio/{targetUserId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAudioForUser(int targetUserId)
    {
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

    /// <summary>
    /// Report a user's voice prompt for policy violations.
    /// Sets moderation status to PENDING_REVIEW so trust & safety can investigate.
    /// POST /api/voice-prompts/report/{targetUserId}
    /// </summary>
    [HttpPost("report/{targetUserId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(int targetUserId, [FromBody] VoicePromptReportRequest request)
    {
        try
        {
            var reporterUserId = GetCurrentUserId();

            if (reporterUserId == targetUserId)
                return BadRequest("Cannot report your own voice prompt");

            var allowedReasons = new[] { "inappropriate", "harassment", "spam", "hate_speech", "other" };
            if (!allowedReasons.Contains(request.Reason?.ToLower()))
                return BadRequest($"Reason must be one of: {string.Join(", ", allowedReasons)}");

            var vp = await _context.VoicePrompts
                .Where(v => v.UserId == targetUserId && !v.IsDeleted)
                .FirstOrDefaultAsync();

            if (vp == null) return NotFound("No voice prompt found for this user");

            // Check for duplicate report
            var existingReport = await _context.VoicePromptReports
                .AnyAsync(r => r.VoicePromptId == vp.Id && r.ReporterUserId == reporterUserId);

            if (existingReport) return Conflict("You have already reported this voice prompt");

            // Create report
            var report = new VoicePromptReport
            {
                VoicePromptId = vp.Id,
                ReporterUserId = reporterUserId,
                TargetUserId = targetUserId,
                Reason = request.Reason!.ToLower(),
                Description = request.Description?.Length > 500
                    ? request.Description[..500]
                    : request.Description,
                CreatedAt = DateTime.UtcNow,
            };

            _context.VoicePromptReports.Add(report);

            // Escalate to PENDING_REVIEW so trust & safety sees it
            if (vp.ModerationStatus != Models.ModerationStatus.Rejected)
            {
                vp.ModerationStatus = Models.ModerationStatus.PendingReview;
            }

            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "Voice prompt {VpId} reported by user {ReporterId} against user {TargetId}: {Reason}",
                vp.Id, reporterUserId, targetUserId, request.Reason);

            return Ok(new { message = "Report submitted. Thank you for helping keep our community safe." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reporting voice prompt");
            return StatusCode(500, "An error occurred while submitting the report");
        }
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

/// <summary>
/// Request body for voice prompt report endpoint.
/// </summary>
public class VoicePromptReportRequest
{
    /// <summary>Reason: inappropriate, harassment, spam, hate_speech, other</summary>
    public string? Reason { get; set; }

    /// <summary>Optional detailed description</summary>
    public string? Description { get; set; }
}
