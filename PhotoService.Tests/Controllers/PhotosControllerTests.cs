using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PhotoService.Controllers;
using PhotoService.Services;
using PhotoService.DTOs;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace PhotoService.Tests.Controllers;

public class PhotosControllerTests
{
    private readonly Mock<IPhotoService> _mockPhotoService;
    private readonly Mock<ILogger<PhotosController>> _mockLogger;
    private readonly Mock<ISafetyServiceClient> _mockSafetyService;
    private readonly Mock<IMatchmakingServiceClient> _mockMatchmakingService;
    private readonly Mock<PhotoService.Data.PhotoContext> _mockContext;
    private readonly PhotosController _controller;

    public PhotosControllerTests()
    {
        _mockPhotoService = new Mock<IPhotoService>();
        _mockLogger = new Mock<ILogger<PhotosController>>();
        _mockSafetyService = new Mock<ISafetyServiceClient>();
        _mockMatchmakingService = new Mock<IMatchmakingServiceClient>();
        
        // Create DbContext mock with minimal configuration
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PhotoService.Data.PhotoContext>()
            .UseInMemoryDatabase(databaseName: "TestPhotoDb_" + Guid.NewGuid())
            .Options;
        _mockContext = new Mock<PhotoService.Data.PhotoContext>(options);

        _controller = new PhotosController(
            _mockPhotoService.Object,
            _mockLogger.Object,
            _mockSafetyService.Object,
            _mockMatchmakingService.Object,
            _mockContext.Object);

        // Setup fake user claims for authentication
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim("sub", "1")
        };
        var identity = new ClaimsIdentity(claims, "TestAuthType");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claimsPrincipal }
        };
    }

    [Fact]
    public async Task UploadPhoto_NoFile_ReturnsBadRequest()
    {
        // Arrange
        var uploadDto = new PhotoUploadDto { Photo = null! };

        // Act
        var result = await _controller.UploadPhoto(uploadDto);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("No photo file provided", badRequestResult.Value?.ToString());
    }

    [Fact]
    public async Task UploadPhoto_FileTooLarge_Returns413()
    {
        // Arrange
        var uploadDto = new PhotoUploadDto
        {
            // Simulate file larger than 10MB limit
            Photo = CreateMockFormFile("large.jpg", "image/jpeg", 11 * 1024 * 1024)
        };

        // Act
        var result = await _controller.UploadPhoto(uploadDto);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413RequestEntityTooLarge, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetUserPhotos_ReturnsPhotoCollection()
    {
        // Arrange
        var expectedSummary = new UserPhotoSummaryDto
        {
            UserId = 1,
            TotalPhotos = 3,
            Photos = new List<PhotoResponseDto>()
        };

        _mockPhotoService
            .Setup(s => s.GetUserPhotosAsync(It.IsAny<int>()))
            .ReturnsAsync(expectedSummary);

        // Act
        var result = await _controller.GetUserPhotos();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.IsType<UserPhotoSummaryDto>(okResult.Value);
        Assert.Equal(3, summary.TotalPhotos);
    }

    [Fact]
    public async Task GetPhoto_InvalidId_ReturnsNotFound()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.GetPhotoAsync(999, It.IsAny<int>()))
            .ReturnsAsync((PhotoResponseDto?)null);

        // Act
        var result = await _controller.GetPhoto(999);

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPrimaryPhoto_NoPhotos_ReturnsNotFound()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.GetPrimaryPhotoAsync(It.IsAny<int>()))
            .ReturnsAsync((PhotoResponseDto?)null);

        // Act
        var result = await _controller.GetPrimaryPhoto();

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetPhotoImage_ValidId_ReturnsFileStream()
    {
        // Arrange
        var imageStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }); // JPEG header
        _mockPhotoService
            .Setup(s => s.GetPhotoStreamAsync(1, "full"))
            .ReturnsAsync((imageStream, "image/jpeg", "photo_1.jpg"));

        // Act
        var result = await _controller.GetPhotoImage(1);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task GetPhotoThumbnail_ValidId_ReturnsThumbnail()
    {
        // Arrange
        var thumbnailStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF });
        _mockPhotoService
            .Setup(s => s.GetPhotoStreamAsync(1, "thumbnail"))
            .ReturnsAsync((thumbnailStream, "image/jpeg", "photo_1_thumb.jpg"));

        // Act
        var result = await _controller.GetPhotoThumbnail(1);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task GetPhotoMedium_ValidId_ReturnsMediumSize()
    {
        // Arrange
        var mediumStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF });
        _mockPhotoService
            .Setup(s => s.GetPhotoStreamAsync(1, "medium"))
            .ReturnsAsync((mediumStream, "image/jpeg", "photo_1_medium.jpg"));

        // Act
        var result = await _controller.GetPhotoMedium(1);

        // Assert
        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task UpdatePhoto_ValidUpdate_ReturnsOk()
    {
        // Arrange
        var updateDto = new PhotoUpdateDto { DisplayOrder = 2 };
        var updatedPhoto = new PhotoResponseDto { Id = 1, DisplayOrder = 2 };

        _mockPhotoService
            .Setup(s => s.UpdatePhotoAsync(1, It.IsAny<int>(), updateDto))
            .ReturnsAsync(updatedPhoto);

        // Act
        var result = await _controller.UpdatePhoto(1, updateDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var photo = Assert.IsType<PhotoResponseDto>(okResult.Value);
        Assert.Equal(2, photo.DisplayOrder);
    }

    [Fact]
    public async Task ReorderPhotos_ValidOrder_ReturnsUpdatedCollection()
    {
        // Arrange
        var reorderDto = new PhotoReorderDto
        {
            Photos = new List<PhotoOrderItemDto>
            {
                new PhotoOrderItemDto { PhotoId = 1, DisplayOrder = 1 },
                new PhotoOrderItemDto { PhotoId = 2, DisplayOrder = 2 }
            }
        };

        var updatedSummary = new UserPhotoSummaryDto
        {
            TotalPhotos = 2
        };

        _mockPhotoService
            .Setup(s => s.ReorderPhotosAsync(It.IsAny<int>(), reorderDto))
            .ReturnsAsync(updatedSummary);

        // Act
        var result = await _controller.ReorderPhotos(reorderDto);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var summary = Assert.IsType<UserPhotoSummaryDto>(okResult.Value);
        Assert.Equal(2, summary.TotalPhotos);
    }

    [Fact]
    public async Task SetPrimaryPhoto_ValidId_ReturnsOk()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.SetPrimaryPhotoAsync(1, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.SetPrimaryPhoto(1);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeletePhoto_ValidId_ReturnsOk()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.DeletePhotoAsync(1, It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.DeletePhoto(1);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CanUploadMorePhotos_UnderLimit_ReturnsTrue()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.CanUserUploadMorePhotosAsync(It.IsAny<int>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CanUploadMorePhotos();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.True((bool)okResult.Value!);
    }

    [Fact]
    public async Task CanUploadMorePhotos_AtLimit_ReturnsFalse()
    {
        // Arrange
        _mockPhotoService
            .Setup(s => s.CanUserUploadMorePhotosAsync(It.IsAny<int>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.CanUploadMorePhotos();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)okResult.Value!);
    }

    [Fact]
    public async Task GetBlurredPhoto_ValidId_ReturnsBlurred()
    {
        // Arrange
        var blurredResponse = new PrivacyImageResponseDto
        {
            ImageData = new byte[] { 0xFF, 0xD8, 0xFF },
            IsBlurred = true,
            ContentType = "image/jpeg"
        };

        _mockPhotoService
            .Setup(s => s.GetBlurredPhotoAsync(1))
            .ReturnsAsync(blurredResponse);

        // Act
        var result = await _controller.GetBlurredPhoto(1);

        // Assert
        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
    }

    [Fact]
    public async Task RegenerateBlurredPhoto_ValidRequest_CreatesNewBlur()
    {
        // Arrange
        var regenerationResult = new BlurRegenerationResultDto
        {
            Success = true,
            BlurIntensity = 0.85
        };

        _mockPhotoService
            .Setup(s => s.RegenerateBlurredPhotoAsync(1, It.IsAny<int>(), 0.85))
            .ReturnsAsync(regenerationResult);

        // Act
        var result = await _controller.RegenerateBlurredPhoto(1, 0.85);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var regenerationDto = Assert.IsType<BlurRegenerationResultDto>(okResult.Value);
        Assert.True(regenerationDto.Success);
    }

    [Fact]
    public async Task GetPhotosForModeration_ReturnsQueue()
    {
        // Arrange
        var moderationPhotos = new List<PhotoResponseDto>
        {
            new PhotoResponseDto { Id = 1, ModerationStatus = "PendingReview" },
            new PhotoResponseDto { Id = 2, ModerationStatus = "PendingReview" }
        };

        _mockPhotoService
            .Setup(s => s.GetPhotosForModerationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((moderationPhotos, 2));

        // Act
        var result = await _controller.GetPhotosForModeration();

        // Assert  
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    // Helper Methods

    private static IFormFile CreateMockFormFile(string fileName, string contentType, long length)
    {
        var fileMock = new Mock<IFormFile>();
        var content = new byte[length];
        var ms = new MemoryStream(content);
        
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.Length).Returns(length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(ms);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, CancellationToken token) => ms.CopyToAsync(stream, token));

        return fileMock.Object;
    }
}
