using Moq;
using Moq.Protected;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoService.Data;
using PhotoService.Models;
using PhotoService.Services;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PhotoService.Tests.Services;

/// <summary>
/// Unit tests for FaceVerificationService — threshold logic, rate limiting,
/// DeepFace integration, error handling.
///
/// These tests use InMemory EF providers and a mock HttpClient so no
/// real DeepFace or MySQL is required.
/// </summary>
public class FaceVerificationServiceTests : IDisposable
{
    private readonly VerificationDbContext _verificationDb;
    private readonly PhotoContext _photoDb;
    private readonly Mock<IHttpClientFactory> _httpClientFactory;
    private readonly Mock<HttpMessageHandler> _httpHandler;
    private readonly ILogger<FaceVerificationService> _logger;

    public FaceVerificationServiceTests()
    {
        // InMemory databases with unique names to avoid cross-test contamination
        var vDbOptions = new DbContextOptionsBuilder<VerificationDbContext>()
            .UseInMemoryDatabase($"VerificationTests_{Guid.NewGuid()}")
            .Options;
        _verificationDb = new VerificationDbContext(vDbOptions);

        var pDbOptions = new DbContextOptionsBuilder<PhotoContext>()
            .UseInMemoryDatabase($"PhotoTests_{Guid.NewGuid()}")
            .Options;
        _photoDb = new PhotoContext(pDbOptions);

        _httpHandler = new Mock<HttpMessageHandler>();
        _httpClientFactory = new Mock<IHttpClientFactory>();
        _httpClientFactory.Setup(f => f.CreateClient("DeepFace"))
            .Returns(new HttpClient(_httpHandler.Object));

        _logger = NullLogger<FaceVerificationService>.Instance;
    }

    public void Dispose()
    {
        _verificationDb.Database.EnsureDeleted();
        _verificationDb.Dispose();
        _photoDb.Database.EnsureDeleted();
        _photoDb.Dispose();
    }

    private FaceVerificationService CreateService() =>
        new(_httpClientFactory.Object, _verificationDb, _photoDb, _logger);

    private void SeedPrimaryPhoto(int userId, int photoId = 1)
    {
        _photoDb.Photos.Add(new Photo
        {
            Id = photoId,
            UserId = userId,
            IsPrimary = true,
            StoredFileName = "profile.jpg",
            FileExtension = ".jpg",
            OriginalFileName = "profile.jpg",
            MimeType = "image/jpeg",
            FileSizeBytes = 10240,
            CreatedAt = DateTime.UtcNow
        });
        _photoDb.SaveChanges();
    }

    private void SetupDeepFaceResponse(double distance, bool verified = true)
    {
        var response = new
        {
            verified,
            distance,
            threshold = 0.30,
            model = "Facenet512",
            facial_areas = new { img1 = new { x = 0, y = 0, w = 100, h = 100 } }
        };
        var json = JsonSerializer.Serialize(response);

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private void SetupDeepFaceError(HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = statusCode });
    }

    private void SetupDeepFaceException(string message = "Connection refused")
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException(message));
    }

    private void SeedRejectedAttempts(int userId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _verificationDb.VerificationAttempts.Add(new VerificationAttempt
            {
                UserId = userId,
                SimilarityScore = 0.20,
                Decision = VerificationDecision.Rejected,
                Result = "Rejected",
                CreatedAt = DateTime.UtcNow
            });
        }
        _verificationDb.SaveChanges();
    }

    private Stream CreateFakeSelfie() =>
        new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // Fake JPEG header

    // ═══════════════════════════════════════════════════════
    // Threshold Tests — Security-Critical
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_HighSimilarity_ReturnsVerified()
    {
        // distance = 0.15 → similarity = 0.85 → ≥ 0.70 → Verified
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.15);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Verified, result.Decision);
        Assert.True(result.SimilarityScore >= 0.70);
        Assert.NotNull(result.AttemptId);
    }

    [Fact]
    public async Task VerifyAsync_ExactlyAtVerifiedThreshold_ReturnsVerified()
    {
        // distance = 0.30 → similarity = 0.70 → exactly at threshold → Verified
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.30);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Verified, result.Decision);
        Assert.Equal(0.70, result.SimilarityScore, precision: 2);
    }

    [Fact]
    public async Task VerifyAsync_BorderlineSimilarity_ReturnsPendingReview()
    {
        // distance = 0.35 → similarity = 0.65 → between 0.60 and 0.70 → PendingReview
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.35);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.PendingReview, result.Decision);
        Assert.True(result.SimilarityScore >= 0.60);
        Assert.True(result.SimilarityScore < 0.70);
    }

    [Fact]
    public async Task VerifyAsync_ExactlyAtPendingThreshold_ReturnsPendingReview()
    {
        // distance = 0.40 → similarity = 0.60 → exactly at pending threshold → PendingReview
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.40);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.PendingReview, result.Decision);
        Assert.Equal(0.60, result.SimilarityScore, precision: 2);
    }

    [Fact]
    public async Task VerifyAsync_LowSimilarity_ReturnsRejected()
    {
        // distance = 0.60 → similarity = 0.40 → < 0.60 → Rejected
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.60);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Rejected, result.Decision);
        Assert.True(result.SimilarityScore < 0.60);
    }

    [Fact]
    public async Task VerifyAsync_VeryDifferentFace_ReturnsRejected()
    {
        // distance = 0.90 → similarity = 0.10 → clearly different person
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.90);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Rejected, result.Decision);
        Assert.True(result.SimilarityScore < 0.30);
    }

    // ═══════════════════════════════════════════════════════
    // Rate Limiting Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_ThreeRejections_ReturnsRateLimited()
    {
        SeedPrimaryPhoto(1);
        SeedRejectedAttempts(1, 3);
        SetupDeepFaceResponse(distance: 0.15); // Would be verified, but rate limited
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.RateLimited, result.Decision);
        Assert.Null(result.AttemptId);
    }

    [Fact]
    public async Task VerifyAsync_TwoRejections_StillAllowed()
    {
        SeedPrimaryPhoto(1);
        SeedRejectedAttempts(1, 2);
        SetupDeepFaceResponse(distance: 0.15);
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Verified, result.Decision); // 3rd attempt works
    }

    [Fact]
    public async Task GetAttemptCountTodayAsync_OnlyCountsRejected()
    {
        // Add mixed attempts — only Rejected should count
        _verificationDb.VerificationAttempts.AddRange(
            new VerificationAttempt { UserId = 1, Result = "Rejected", Decision = VerificationDecision.Rejected, SimilarityScore = 0.1, CreatedAt = DateTime.UtcNow },
            new VerificationAttempt { UserId = 1, Result = "Verified", Decision = VerificationDecision.Verified, SimilarityScore = 0.85, CreatedAt = DateTime.UtcNow },
            new VerificationAttempt { UserId = 1, Result = "Pending", Decision = VerificationDecision.PendingReview, SimilarityScore = 0.65, CreatedAt = DateTime.UtcNow }
        );
        await _verificationDb.SaveChangesAsync();
        var svc = CreateService();

        var count = await svc.GetAttemptCountTodayAsync(1);

        Assert.Equal(1, count); // Only the Rejected one
    }

    [Fact]
    public async Task GetAttemptCountTodayAsync_IgnoresYesterdayAttempts()
    {
        _verificationDb.VerificationAttempts.Add(new VerificationAttempt
        {
            UserId = 1,
            Result = "Rejected",
            Decision = VerificationDecision.Rejected,
            SimilarityScore = 0.1,
            CreatedAt = DateTime.UtcNow.AddDays(-1) // Yesterday
        });
        await _verificationDb.SaveChangesAsync();
        var svc = CreateService();

        var count = await svc.GetAttemptCountTodayAsync(1);

        Assert.Equal(0, count);
    }

    // ═══════════════════════════════════════════════════════
    // Missing Profile Photo Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_NoProfilePhoto_ReturnsRejected()
    {
        // No photo seeded for this user
        var svc = CreateService();

        var result = await svc.VerifyAsync(99, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Rejected, result.Decision);
        Assert.Contains("profile photo", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════
    // DeepFace Error Handling Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_DeepFaceReturnsError_DoesNotCountAsAttempt()
    {
        SeedPrimaryPhoto(1);
        SetupDeepFaceError();
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Error, result.Decision);
        Assert.Null(result.AttemptId);
        // Confirm nothing was saved to DB
        Assert.Empty(_verificationDb.VerificationAttempts.Where(a => a.UserId == 1));
    }

    [Fact]
    public async Task VerifyAsync_DeepFaceThrows_DoesNotCountAsAttempt()
    {
        SeedPrimaryPhoto(1);
        SetupDeepFaceException();
        var svc = CreateService();

        var result = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        Assert.Equal(VerificationDecision.Error, result.Decision);
        Assert.Null(result.AttemptId);
    }

    [Fact]
    public async Task VerifyAsync_ErrorDoesNotBurnDailySlot()
    {
        SeedPrimaryPhoto(1);

        // 2 rejections + 1 error
        SeedRejectedAttempts(1, 2);
        SetupDeepFaceError();
        var svc = CreateService();

        var errorResult = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");
        Assert.Equal(VerificationDecision.Error, errorResult.Decision);

        // Should still be allowed (only 2 rejected, error didn't count)
        SetupDeepFaceResponse(distance: 0.15);
        svc = CreateService(); // Fresh HttpClient
        var retryResult = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");
        Assert.Equal(VerificationDecision.Verified, retryResult.Decision);
    }

    // ═══════════════════════════════════════════════════════
    // DB Persistence Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_Verified_SavesAttemptToDb()
    {
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.15);
        var svc = CreateService();

        await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        var attempt = await _verificationDb.VerificationAttempts
            .FirstOrDefaultAsync(a => a.UserId == 1);
        Assert.NotNull(attempt);
        Assert.Equal(VerificationDecision.Verified, attempt.Decision);
        Assert.Equal("Verified", attempt.Result);
        Assert.True(attempt.SimilarityScore >= 0.70);
    }

    [Fact]
    public async Task VerifyAsync_Rejected_SavesAttemptWithReason()
    {
        SeedPrimaryPhoto(1);
        SetupDeepFaceResponse(distance: 0.60);
        var svc = CreateService();

        await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");

        var attempt = await _verificationDb.VerificationAttempts
            .FirstOrDefaultAsync(a => a.UserId == 1);
        Assert.NotNull(attempt);
        Assert.Equal(VerificationDecision.Rejected, attempt.Decision);
        Assert.NotNull(attempt.RejectionReason);
        Assert.Contains("match", attempt.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestAttemptAsync_ReturnsNewestAttempt()
    {
        _verificationDb.VerificationAttempts.AddRange(
            new VerificationAttempt { UserId = 1, Result = "Rejected", Decision = VerificationDecision.Rejected, SimilarityScore = 0.1, CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new VerificationAttempt { UserId = 1, Result = "Verified", Decision = VerificationDecision.Verified, SimilarityScore = 0.85, CreatedAt = DateTime.UtcNow }
        );
        await _verificationDb.SaveChangesAsync();
        var svc = CreateService();

        var latest = await svc.GetLatestAttemptAsync(1);

        Assert.NotNull(latest);
        Assert.Equal(VerificationDecision.Verified, latest.Decision);
    }

    [Fact]
    public async Task GetLatestAttemptAsync_NoAttempts_ReturnsNull()
    {
        var svc = CreateService();
        var latest = await svc.GetLatestAttemptAsync(999);
        Assert.Null(latest);
    }

    // ═══════════════════════════════════════════════════════
    // Multi-user Isolation Tests
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyAsync_RateLimitIsPerUser()
    {
        SeedPrimaryPhoto(1);
        SeedPrimaryPhoto(2, photoId: 2);
        SeedRejectedAttempts(1, 3); // User 1 exhausted
        SetupDeepFaceResponse(distance: 0.15);
        var svc = CreateService();

        // User 1 blocked
        var result1 = await svc.VerifyAsync(1, CreateFakeSelfie(), "selfie.jpg");
        Assert.Equal(VerificationDecision.RateLimited, result1.Decision);

        // User 2 should be fine
        var result2 = await svc.VerifyAsync(2, CreateFakeSelfie(), "selfie.jpg");
        Assert.Equal(VerificationDecision.Verified, result2.Decision);
    }
}
