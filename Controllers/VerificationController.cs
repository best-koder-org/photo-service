using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoService.Services;

namespace PhotoService.Controllers;

/// <summary>
/// Face verification endpoint for self-hosted DeepFace verification badges.
/// POST /api/verification/submit — accepts selfie, compares to profile photo.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VerificationController : ControllerBase
{
    private readonly IFaceVerificationService _verificationService;
    private readonly ILogger<VerificationController> _logger;

    public VerificationController(
        IFaceVerificationService verificationService,
        ILogger<VerificationController> logger)
    {
        _verificationService = verificationService;
        _logger = logger;
    }

    /// <summary>
    /// Submit a selfie for face verification against profile photo.
    /// Uses DeepFace Facenet512 with anti-spoofing.
    /// Rate limited: max 3 attempts per 24 hours.
    /// </summary>
    [HttpPost("submit")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB max selfie
    public async Task<IActionResult> SubmitVerification(IFormFile selfie)
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        if (selfie == null || selfie.Length == 0)
            return BadRequest(new { error = "Selfie image is required" });

        if (!selfie.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "File must be an image" });

        _logger.LogInformation("Verification attempt for user {UserId}", userId);

        using var stream = selfie.OpenReadStream();
        var result = await _verificationService.VerifyAsync(userId, stream, selfie.FileName);

        return result.Decision switch
        {
            VerificationDecision.Verified => Ok(new
            {
                status = "verified",
                message = result.Message,
                similarity = result.SimilarityScore,
                attemptId = result.AttemptId
            }),
            VerificationDecision.PendingReview => Ok(new
            {
                status = "pending",
                message = result.Message,
                similarity = result.SimilarityScore,
                attemptId = result.AttemptId
            }),
            VerificationDecision.Rejected => Ok(new
            {
                status = "rejected",
                message = result.Message,
                similarity = result.SimilarityScore,
                attemptId = result.AttemptId
            }),
            VerificationDecision.RateLimited => StatusCode(429, new
            {
                status = "rate_limited",
                message = result.Message
            }),
            _ => StatusCode(500, new
            {
                status = "error",
                message = result.Message
            })
        };
    }

    /// <summary>
    /// Get current verification status for the authenticated user.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var latest = await _verificationService.GetLatestAttemptAsync(userId);
        var attemptsToday = await _verificationService.GetAttemptCountTodayAsync(userId);

        return Ok(new
        {
            isVerified = latest?.Decision == VerificationDecision.Verified,
            lastAttempt = latest != null ? new
            {
                result = latest.Result,
                similarity = latest.SimilarityScore,
                date = latest.CreatedAt
            } : null,
            attemptsRemainingToday = Math.Max(0, 3 - attemptsToday)
        });
    }
}
