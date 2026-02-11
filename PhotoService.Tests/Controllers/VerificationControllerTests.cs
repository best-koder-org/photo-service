using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using PhotoService.Controllers;
using PhotoService.Services;
using PhotoService.Models;

namespace PhotoService.Tests.Controllers;

public class VerificationControllerTests
{
    private readonly Mock<IFaceVerificationService> _mockVerification;
    private readonly Mock<ILogger<VerificationController>> _mockLogger;
    private readonly VerificationController _controller;

    public VerificationControllerTests()
    {
        _mockVerification = new Mock<IFaceVerificationService>();
        _mockLogger = new Mock<ILogger<VerificationController>>();
        _controller = new VerificationController(_mockVerification.Object, _mockLogger.Object);
    }

    private void SetupAuth(string userId)
    {
        var claims = new List<Claim> { new Claim("sub", userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private void SetupNoAuth()
    {
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) }
        };
    }

    private static IFormFile CreateMockSelfie(string name = "selfie.jpg", string contentType = "image/jpeg", int size = 1024)
    {
        var stream = new MemoryStream(new byte[size]);
        return new FormFile(stream, 0, size, "selfie", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    // --- SubmitVerification ---

    [Fact]
    public async Task SubmitVerification_Verified_ReturnsOkWithVerifiedStatus()
    {
        // Arrange
        SetupAuth("1");
        var selfie = CreateMockSelfie();
        _mockVerification.Setup(v => v.VerifyAsync(1, It.IsAny<Stream>(), "selfie.jpg"))
            .ReturnsAsync(new VerificationResult(VerificationDecision.Verified, 0.85, "Verification successful!", 42));

        // Act
        var result = await _controller.SubmitVerification(selfie);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var value = okResult.Value!;
        Assert.Contains("verified", value.ToString()!.ToLower());
    }

    [Fact]
    public async Task SubmitVerification_Rejected_ReturnsOkWithRejectedStatus()
    {
        // Arrange
        SetupAuth("1");
        var selfie = CreateMockSelfie();
        _mockVerification.Setup(v => v.VerifyAsync(1, It.IsAny<Stream>(), "selfie.jpg"))
            .ReturnsAsync(new VerificationResult(VerificationDecision.Rejected, 0.15, "Face didn't match", 43));

        // Act
        var result = await _controller.SubmitVerification(selfie);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("rejected", okResult.Value!.ToString()!.ToLower());
    }

    [Fact]
    public async Task SubmitVerification_PendingReview_ReturnsOkWithPendingStatus()
    {
        // Arrange
        SetupAuth("1");
        var selfie = CreateMockSelfie();
        _mockVerification.Setup(v => v.VerifyAsync(1, It.IsAny<Stream>(), "selfie.jpg"))
            .ReturnsAsync(new VerificationResult(VerificationDecision.PendingReview, 0.35, "Borderline", 44));

        // Act
        var result = await _controller.SubmitVerification(selfie);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("pending", okResult.Value!.ToString()!.ToLower());
    }

    [Fact]
    public async Task SubmitVerification_RateLimited_Returns429()
    {
        // Arrange
        SetupAuth("1");
        var selfie = CreateMockSelfie();
        _mockVerification.Setup(v => v.VerifyAsync(1, It.IsAny<Stream>(), "selfie.jpg"))
            .ReturnsAsync(new VerificationResult(VerificationDecision.RateLimited, 0, "Max attempts reached", null));

        // Act
        var result = await _controller.SubmitVerification(selfie);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, statusResult.StatusCode);
    }

    [Fact]
    public async Task SubmitVerification_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        SetupNoAuth();
        var selfie = CreateMockSelfie();

        // Act
        var result = await _controller.SubmitVerification(selfie);

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task SubmitVerification_NullSelfie_ReturnsBadRequest()
    {
        // Arrange
        SetupAuth("1");

        // Act
        var result = await _controller.SubmitVerification(null!);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitVerification_NonImageFile_ReturnsBadRequest()
    {
        // Arrange
        SetupAuth("1");
        var textFile = CreateMockSelfie("file.txt", "text/plain");

        // Act
        var result = await _controller.SubmitVerification(textFile);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitVerification_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        SetupAuth("1");
        var emptyFile = CreateMockSelfie(size: 0);

        // Act
        var result = await _controller.SubmitVerification(emptyFile);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    // --- GetStatus ---

    [Fact]
    public async Task GetStatus_VerifiedUser_ReturnsVerifiedStatus()
    {
        // Arrange
        SetupAuth("1");
        var attempt = new VerificationAttempt
        {
            UserId = 1,
            Decision = VerificationDecision.Verified,
            SimilarityScore = 0.85,
            Result = "Verified",
            CreatedAt = DateTime.UtcNow
        };
        _mockVerification.Setup(v => v.GetLatestAttemptAsync(1)).ReturnsAsync(attempt);
        _mockVerification.Setup(v => v.GetAttemptCountTodayAsync(1)).ReturnsAsync(1);

        // Act
        var result = await _controller.GetStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("true", okResult.Value!.ToString()!.ToLower()); // isVerified = true
    }

    [Fact]
    public async Task GetStatus_NoAttempts_ReturnsNotVerified()
    {
        // Arrange
        SetupAuth("1");
        _mockVerification.Setup(v => v.GetLatestAttemptAsync(1)).ReturnsAsync((VerificationAttempt?)null);
        _mockVerification.Setup(v => v.GetAttemptCountTodayAsync(1)).ReturnsAsync(0);

        // Act
        var result = await _controller.GetStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("attemptsRemainingToday = 3", okResult.Value!.ToString()!);
    }

    [Fact]
    public async Task GetStatus_NoAuth_ReturnsUnauthorized()
    {
        // Arrange
        SetupNoAuth();

        // Act
        var result = await _controller.GetStatus();

        // Assert
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetStatus_ThreeAttemptsToday_ReturnsZeroRemaining()
    {
        // Arrange
        SetupAuth("1");
        _mockVerification.Setup(v => v.GetLatestAttemptAsync(1))
            .ReturnsAsync(new VerificationAttempt { UserId = 1, Decision = VerificationDecision.Rejected, SimilarityScore = 0.1, Result = "Rejected", CreatedAt = DateTime.UtcNow });
        _mockVerification.Setup(v => v.GetAttemptCountTodayAsync(1)).ReturnsAsync(3);

        // Act
        var result = await _controller.GetStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("attemptsRemainingToday = 0", okResult.Value!.ToString()!);
    }
}
