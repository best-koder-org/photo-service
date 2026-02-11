using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PhotoService.Controllers;
using PhotoService.Data;

namespace PhotoService.Tests.Controllers;

public class HealthControllerTests : IDisposable
{
    private readonly PhotoContext _context;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly Mock<ILogger<HealthController>> _mockLogger;
    private readonly HealthController _controller;
    private readonly string _tempDir;

    public HealthControllerTests()
    {
        var options = new DbContextOptionsBuilder<PhotoContext>()
            .UseInMemoryDatabase($"HealthController_{Guid.NewGuid()}")
            .Options;
        _context = new PhotoContext(options);

        _tempDir = Path.Combine(Path.GetTempPath(), $"photo-health-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDir);

        _mockLogger = new Mock<ILogger<HealthController>>();
        _controller = new HealthController(_context, _mockEnv.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetHealth_ReturnsOk_WithHealthyStatus()
    {
        // Act
        var result = _controller.GetHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<HealthResponse>(okResult.Value);
        Assert.Equal("Healthy", response.Status);
        Assert.Equal("PhotoService", response.Service);
        Assert.True(response.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public async Task GetDetailedHealth_AllHealthy_Returns200()
    {
        // Arrange — storage dir will be created by the controller
        var uploadsDir = Path.Combine(_tempDir, "uploads", "photos");
        Directory.CreateDirectory(uploadsDir);

        // Act
        var result = await _controller.GetDetailedHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DetailedHealthResponse>(okResult.Value);
        Assert.Equal("Healthy", response.Status);
        Assert.True(response.Database.Healthy);
        Assert.True(response.Storage.Healthy);
        Assert.Equal(0, response.Stats.TotalPhotos);
    }

    [Fact]
    public async Task GetDetailedHealth_WithPhotos_ReportsPhotoCount()
    {
        // Arrange
        _context.Photos.Add(new PhotoService.Models.Photo { UserId = 1, OriginalFileName = "a.jpg", StoredFileName = "stored_a.jpg", FileExtension = ".jpg", FileSizeBytes = 100, MimeType = "image/jpeg", CreatedAt = DateTime.UtcNow });
        _context.Photos.Add(new PhotoService.Models.Photo { UserId = 2, OriginalFileName = "b.jpg", StoredFileName = "stored_b.jpg", FileExtension = ".jpg", FileSizeBytes = 100, MimeType = "image/jpeg", CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var uploadsDir = Path.Combine(_tempDir, "uploads", "photos");
        Directory.CreateDirectory(uploadsDir);

        // Act
        var result = await _controller.GetDetailedHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DetailedHealthResponse>(okResult.Value);
        Assert.Equal(2, response.Stats.TotalPhotos);
    }

    [Fact]
    public async Task GetDetailedHealth_StorageMissing_CreatesDirectory()
    {
        // Arrange — don't pre-create the directory; controller should create it

        // Act
        var result = await _controller.GetDetailedHealth();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DetailedHealthResponse>(okResult.Value);
        Assert.True(response.Storage.Healthy);
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "uploads", "photos")));
    }
}
