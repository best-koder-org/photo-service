using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PhotoService.Controllers;
using PhotoService.Data;
using PhotoService.Models;

namespace PhotoService.Tests.Controllers;

/// <summary>
/// Tests for PhotoDeletionController — cascade photo deletion for account removal.
/// Note: PhotoContext overrides SaveChanges to convert hard deletes to soft deletes,
/// so we assert IsDeleted flag rather than row removal.
/// </summary>
public class PhotoDeletionControllerTests : IDisposable
{
    private readonly PhotoContext _context;
    private readonly Mock<ILogger<PhotoDeletionController>> _mockLogger;
    private readonly PhotoDeletionController _controller;

    public PhotoDeletionControllerTests()
    {
        var options = new DbContextOptionsBuilder<PhotoContext>()
            .UseInMemoryDatabase($"PhotoDeletion_{Guid.NewGuid()}")
            .Options;
        _context = new PhotoContext(options);
        _mockLogger = new Mock<ILogger<PhotoDeletionController>>();
        _controller = new PhotoDeletionController(_context, _mockLogger.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static Photo CreatePhoto(int userId, string name)
    {
        return new Photo
        {
            UserId = userId,
            OriginalFileName = name,
            StoredFileName = $"stored_{name}",
            FileExtension = ".jpg",
            FileSizeBytes = 1024,
            MimeType = "image/jpeg",
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task DeleteUserPhotos_WithPhotos_ReturnsCorrectCount()
    {
        // Arrange
        _context.Photos.AddRange(CreatePhoto(42, "p1.jpg"), CreatePhoto(42, "p2.jpg"), CreatePhoto(42, "p3.jpg"));
        _context.Photos.Add(CreatePhoto(99, "other.jpg"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.DeleteUserPhotos(42);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("3", okResult.Value);

        // PhotoContext uses soft-delete — photos are marked IsDeleted, not removed
        var softDeleted = await _context.Photos.Where(p => p.UserId == 42 && p.IsDeleted).CountAsync();
        Assert.Equal(3, softDeleted);

        // Other user's photo is unaffected
        var untouched = await _context.Photos.Where(p => p.UserId == 99 && !p.IsDeleted).CountAsync();
        Assert.Equal(1, untouched);
    }

    [Fact]
    public async Task DeleteUserPhotos_NoPhotos_ReturnsZero()
    {
        // Act
        var result = await _controller.DeleteUserPhotos(999);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("0", okResult.Value);
    }

    [Fact]
    public async Task DeleteUserPhotos_OnlyAffectsTargetUser()
    {
        // Arrange
        _context.Photos.AddRange(CreatePhoto(1, "a.jpg"), CreatePhoto(2, "b.jpg"), CreatePhoto(3, "c.jpg"));
        await _context.SaveChangesAsync();

        // Act
        await _controller.DeleteUserPhotos(2);

        // Assert — user 2's photo is soft-deleted, others untouched
        var user2Photos = await _context.Photos.Where(p => p.UserId == 2).ToListAsync();
        Assert.Single(user2Photos);
        Assert.True(user2Photos[0].IsDeleted);

        var otherPhotos = await _context.Photos.Where(p => p.UserId != 2).ToListAsync();
        Assert.Equal(2, otherPhotos.Count);
        Assert.All(otherPhotos, p => Assert.False(p.IsDeleted));
    }
}
