using Xunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PhotoService.Controllers;
using PhotoService.Data;
using PhotoService.Models;
using System.Security.Claims;
using System.Text;
using Moq;

namespace PhotoService.Tests.Controllers;

/// <summary>
/// Unit tests for VoicePromptsController.
/// Uses InMemoryDatabase (no real MySQL needed) and fake ClaimsPrincipal for auth.
/// Tests cover: upload validation, CRUD, report endpoint, moderation filtering.
/// </summary>
public class VoicePromptsControllerTests : IDisposable
{
    private readonly PhotoContext _context;
    private readonly Mock<ILogger<VoicePromptsController>> _mockLogger;
    private readonly VoicePromptsController _controller;
    private readonly string _testUploadDir;

    public VoicePromptsControllerTests()
    {
        var options = new DbContextOptionsBuilder<PhotoContext>()
            .UseInMemoryDatabase(databaseName: "TestVoiceDb_" + Guid.NewGuid())
            .Options;
        _context = new PhotoContext(options);
        _mockLogger = new Mock<ILogger<VoicePromptsController>>();

        _controller = new VoicePromptsController(_context, _mockLogger.Object);

        // Setup fake user with userId=1
        SetupUser("1");

        // Create temp upload dir for file-based tests
        _testUploadDir = Path.Combine(Path.GetTempPath(), "voice-test-" + Guid.NewGuid());
        Directory.CreateDirectory(_testUploadDir);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        if (Directory.Exists(_testUploadDir))
            Directory.Delete(_testUploadDir, true);
    }

    private void SetupUser(string userId)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("sub", userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    private static IFormFile CreateMockAudioFile(string name, string contentType, int sizeBytes)
    {
        var content = new byte[sizeBytes];
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, sizeBytes, "audio", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private async Task<VoicePrompt> SeedVoicePromptAsync(int userId, string status = "AUTO_APPROVED")
    {
        var vp = new VoicePrompt
        {
            UserId = userId,
            StoredFileName = $"{userId}_test_{Guid.NewGuid():N}.m4a",
            FileSizeBytes = 50000,
            DurationSeconds = 10,
            MimeType = "audio/mp4",
            ModerationStatus = status,
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
        };
        _context.VoicePrompts.Add(vp);
        await _context.SaveChangesAsync();
        return vp;
    }

    // ──────────────── Upload Tests ────────────────

    [Fact]
    public async Task Upload_NoFile_ReturnsBadRequest()
    {
        var result = await _controller.Upload(null!);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("No audio file", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Upload_FileTooLarge_ReturnsBadRequest()
    {
        var file = CreateMockAudioFile("big.m4a", "audio/mp4", 3 * 1024 * 1024); // 3MB > 2MB limit
        var result = await _controller.Upload(file);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("exceeds", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Upload_InvalidMimeType_ReturnsBadRequest()
    {
        var file = CreateMockAudioFile("bad.txt", "text/plain", 1000);
        var result = await _controller.Upload(file);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Invalid audio format", badRequest.Value?.ToString());
    }

    // ──────────────── GetMeta Tests ────────────────

    [Fact]
    public async Task GetMeta_ExistingPrompt_ReturnsOk()
    {
        var vp = await SeedVoicePromptAsync(42);

        var result = await _controller.GetMeta(42);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetMeta_NoPrompt_ReturnsNotFound()
    {
        var result = await _controller.GetMeta(999);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMeta_RejectedPrompt_ReturnsNotFound()
    {
        await SeedVoicePromptAsync(42, ModerationStatus.Rejected);

        var result = await _controller.GetMeta(42);
        Assert.IsType<NotFoundResult>(result);
    }

    // ──────────────── Delete Tests ────────────────

    [Fact]
    public async Task Delete_ExistingPrompt_SoftDeletes()
    {
        await SeedVoicePromptAsync(1); // userId matches the auth user

        var result = await _controller.Delete();

        var okResult = Assert.IsType<OkObjectResult>(result);
        // Verify soft delete in DB
        var vp = await _context.VoicePrompts.FirstAsync(v => v.UserId == 1);
        Assert.True(vp.IsDeleted);
        Assert.NotNull(vp.DeletedAt);
    }

    [Fact]
    public async Task Delete_NoPrompt_ReturnsNotFound()
    {
        var result = await _controller.Delete();
        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ──────────────── Report Tests ────────────────

    [Fact]
    public async Task Report_ValidReport_ReturnsOkAndSetsPendingReview()
    {
        // Reporter is user 1, target is user 42
        var vp = await SeedVoicePromptAsync(42);
        SetupUser("1"); // re-setup to ensure fresh context

        var request = new VoicePromptReportRequest
        {
            Reason = "harassment",
            Description = "Offensive language"
        };

        var result = await _controller.Report(42, request);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Report submitted", okResult.Value?.ToString());

        // Check the VP was escalated to PENDING_REVIEW
        var updatedVp = await _context.VoicePrompts.FindAsync(vp.Id);
        Assert.Equal(ModerationStatus.PendingReview, updatedVp!.ModerationStatus);

        // Check the report record
        var report = await _context.VoicePromptReports.FirstAsync();
        Assert.Equal("harassment", report.Reason);
        Assert.Equal(1, report.ReporterUserId);
        Assert.Equal(42, report.TargetUserId);
    }

    [Fact]
    public async Task Report_SelfReport_ReturnsBadRequest()
    {
        await SeedVoicePromptAsync(1);

        var request = new VoicePromptReportRequest { Reason = "spam" };
        var result = await _controller.Report(1, request);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Report_InvalidReason_ReturnsBadRequest()
    {
        await SeedVoicePromptAsync(42);

        var request = new VoicePromptReportRequest { Reason = "just-because" };
        var result = await _controller.Report(42, request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Reason must be one of", badRequest.Value?.ToString());
    }

    [Fact]
    public async Task Report_NoTargetPrompt_ReturnsNotFound()
    {
        var request = new VoicePromptReportRequest { Reason = "spam" };
        var result = await _controller.Report(999, request);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Report_DuplicateReport_ReturnsConflict()
    {
        var vp = await SeedVoicePromptAsync(42);

        // First report
        _context.VoicePromptReports.Add(new VoicePromptReport
        {
            VoicePromptId = vp.Id,
            ReporterUserId = 1,
            TargetUserId = 42,
            Reason = "spam",
        });
        await _context.SaveChangesAsync();

        // Duplicate
        var request = new VoicePromptReportRequest { Reason = "harassment" };
        var result = await _controller.Report(42, request);

        Assert.IsType<ConflictObjectResult>(result);
    }

    // ──────────────── Moderation Filtering Tests ────────────────

    [Fact]
    public async Task GetMeta_ApprovedPrompt_IsVisible()
    {
        await SeedVoicePromptAsync(42, ModerationStatus.Approved);

        var result = await _controller.GetMeta(42);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetMeta_AutoApprovedPrompt_IsVisible()
    {
        await SeedVoicePromptAsync(42, ModerationStatus.AutoApproved);

        var result = await _controller.GetMeta(42);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetMeta_PendingReviewPrompt_IsVisible()
    {
        // PENDING_REVIEW should still be visible (only REJECTED is filtered)
        await SeedVoicePromptAsync(42, ModerationStatus.PendingReview);

        var result = await _controller.GetMeta(42);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetMeta_DeletedPrompt_ReturnsNotFound()
    {
        var vp = await SeedVoicePromptAsync(42);
        vp.IsDeleted = true;
        vp.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var result = await _controller.GetMeta(42);
        Assert.IsType<NotFoundResult>(result);
    }
}
