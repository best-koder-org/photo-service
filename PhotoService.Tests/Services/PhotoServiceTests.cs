using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PhotoService.Data;
using PhotoService.Models;
using PhotoService.Services;
using Xunit;

namespace PhotoService.Tests.Services;

public class PhotoServiceTests : IDisposable
{
    private readonly PhotoContext _context;
    private readonly Mock<IImageProcessingService> _mockImageProcessing;
    private readonly Mock<IStorageService> _mockStorage;
    private readonly Mock<ILogger<PhotoService.Services.PhotoService>> _mockLogger;
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly PhotoService.Services.PhotoService _sut;

    public PhotoServiceTests()
    {
        var options = new DbContextOptionsBuilder<PhotoContext>()
            .UseInMemoryDatabase(databaseName: $"PhotoTests_{Guid.NewGuid()}")
            .Options;

        _context = new PhotoContext(options);
        _mockImageProcessing = new Mock<IImageProcessingService>();
        _mockStorage = new Mock<IStorageService>();
        _mockLogger = new Mock<ILogger<PhotoService.Services.PhotoService>>();
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();

        _sut = new PhotoService.Services.PhotoService(
            _context,
            _mockImageProcessing.Object,
            _mockStorage.Object,
            _mockLogger.Object,
            _mockHttpContextAccessor.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ── Helper ──────────────────────────────────────────────
    private Photo CreatePhoto(int userId, int id, bool isPrimary = false, bool isDeleted = false, int displayOrder = 0)
    {
        return new Photo
        {
            Id = id,
            UserId = userId,
            IsPrimary = isPrimary,
            IsDeleted = isDeleted,
            DisplayOrder = displayOrder,
            OriginalFileName = $"photo_{id}.jpg",
            StoredFileName = $"stored_{id}.jpg",
            FileExtension = ".jpg",
            FileSizeBytes = 1024,
            Width = 800,
            Height = 600,
            ModerationStatus = ModerationStatus.Approved,
            QualityScore = 80,
            CreatedAt = DateTime.UtcNow.AddDays(-id),
            UpdatedAt = DateTime.UtcNow.AddDays(-id)
        };
    }

    // ═══════════════════════════════════════════════════════
    //  SetPrimaryPhotoAsync — validates the AsNoTracking bug fix
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task SetPrimaryPhoto_SetsNewPrimary_UnsetsOldPrimary()
    {
        // Arrange: user has photo1 as primary and photo2 as non-primary
        var photo1 = CreatePhoto(userId: 1, id: 1, isPrimary: true);
        var photo2 = CreatePhoto(userId: 1, id: 2, isPrimary: false);
        _context.Photos.AddRange(photo1, photo2);
        await _context.SaveChangesAsync();

        // Act: set photo2 as primary
        var result = await _sut.SetPrimaryPhotoAsync(photoId: 2, userId: 1);

        // Assert
        Assert.True(result);

        // Re-query from DB to confirm persistence
        var updatedPhoto1 = await _context.Photos.FindAsync(1);
        var updatedPhoto2 = await _context.Photos.FindAsync(2);

        Assert.False(updatedPhoto1!.IsPrimary, "Old primary should be unset");
        Assert.True(updatedPhoto2!.IsPrimary, "New photo should be primary");
    }

    [Fact]
    public async Task SetPrimaryPhoto_MultiplePrimaries_AllGetUnset()
    {
        // Arrange: corrupt state with 3 primaries
        var photo1 = CreatePhoto(userId: 1, id: 1, isPrimary: true);
        var photo2 = CreatePhoto(userId: 1, id: 2, isPrimary: true);
        var photo3 = CreatePhoto(userId: 1, id: 3, isPrimary: true);
        var photo4 = CreatePhoto(userId: 1, id: 4, isPrimary: false);
        _context.Photos.AddRange(photo1, photo2, photo3, photo4);
        await _context.SaveChangesAsync();

        // Act: set photo4 as primary
        var result = await _sut.SetPrimaryPhotoAsync(photoId: 4, userId: 1);

        // Assert: only photo4 is primary
        Assert.True(result);
        var allPhotos = await _context.Photos.ToListAsync();
        var primaries = allPhotos.Where(p => p.IsPrimary).ToList();
        Assert.Single(primaries);
        Assert.Equal(4, primaries[0].Id);
    }

    [Fact]
    public async Task SetPrimaryPhoto_NonexistentPhoto_ReturnsFalse()
    {
        var result = await _sut.SetPrimaryPhotoAsync(photoId: 999, userId: 1);
        Assert.False(result);
    }

    [Fact]
    public async Task SetPrimaryPhoto_WrongUser_ReturnsFalse()
    {
        var photo = CreatePhoto(userId: 1, id: 1);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.SetPrimaryPhotoAsync(photoId: 1, userId: 99);
        Assert.False(result);
    }

    [Fact]
    public async Task SetPrimaryPhoto_DeletedPhoto_ReturnsFalse()
    {
        var photo = CreatePhoto(userId: 1, id: 1, isDeleted: true);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.SetPrimaryPhotoAsync(photoId: 1, userId: 1);
        Assert.False(result);
    }

    [Fact]
    public async Task SetPrimaryPhoto_DoesNotAffectOtherUsers()
    {
        // Arrange: user1 has primary photo, user2 also has primary photo
        var u1Photo = CreatePhoto(userId: 1, id: 1, isPrimary: true);
        var u2Photo = CreatePhoto(userId: 2, id: 2, isPrimary: true);
        var u1NewPhoto = CreatePhoto(userId: 1, id: 3, isPrimary: false);
        _context.Photos.AddRange(u1Photo, u2Photo, u1NewPhoto);
        await _context.SaveChangesAsync();

        // Act: set user1's photo3 as primary
        await _sut.SetPrimaryPhotoAsync(photoId: 3, userId: 1);

        // Assert: user2's primary is untouched
        var user2Photo = await _context.Photos.FindAsync(2);
        Assert.True(user2Photo!.IsPrimary, "Other user's primary must not change");
    }

    // ═══════════════════════════════════════════════════════
    //  DeletePhotoAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task DeletePhoto_SoftDeletesAndSetsTimestamp()
    {
        var photo = CreatePhoto(userId: 1, id: 1);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.DeletePhotoAsync(photoId: 1, userId: 1);

        Assert.True(result);
        var deleted = await _context.Photos.FindAsync(1);
        Assert.True(deleted!.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
    }

    [Fact]
    public async Task DeletePhoto_PrimaryDeleted_NextBecomePrimary()
    {
        var photo1 = CreatePhoto(userId: 1, id: 1, isPrimary: true, displayOrder: 0);
        var photo2 = CreatePhoto(userId: 1, id: 2, displayOrder: 1);
        var photo3 = CreatePhoto(userId: 1, id: 3, displayOrder: 2);
        _context.Photos.AddRange(photo1, photo2, photo3);
        await _context.SaveChangesAsync();

        await _sut.DeletePhotoAsync(photoId: 1, userId: 1);

        var next = await _context.Photos.FindAsync(2);
        Assert.True(next!.IsPrimary, "Next photo by DisplayOrder should become primary");
    }

    [Fact]
    public async Task DeletePhoto_NonexistentPhoto_ReturnsFalse()
    {
        var result = await _sut.DeletePhotoAsync(photoId: 999, userId: 1);
        Assert.False(result);
    }

    [Fact]
    public async Task DeletePhoto_WrongUser_ReturnsFalse()
    {
        var photo = CreatePhoto(userId: 1, id: 1);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.DeletePhotoAsync(photoId: 1, userId: 99);
        Assert.False(result);
    }

    // ═══════════════════════════════════════════════════════
    //  GetUserPhotosAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task GetUserPhotos_ExcludesDeletedPhotos()
    {
        var photo1 = CreatePhoto(userId: 1, id: 1);
        var photo2 = CreatePhoto(userId: 1, id: 2, isDeleted: true);
        _context.Photos.AddRange(photo1, photo2);
        await _context.SaveChangesAsync();

        var summary = await _sut.GetUserPhotosAsync(userId: 1);

        Assert.Equal(1, summary.TotalPhotos);
        Assert.Single(summary.Photos);
        Assert.Equal(1, summary.Photos[0].Id);
    }

    [Fact]
    public async Task GetUserPhotos_OrdersByDisplayOrder()
    {
        var photo1 = CreatePhoto(userId: 1, id: 1, displayOrder: 3);
        var photo2 = CreatePhoto(userId: 1, id: 2, displayOrder: 1);
        var photo3 = CreatePhoto(userId: 1, id: 3, displayOrder: 2);
        _context.Photos.AddRange(photo1, photo2, photo3);
        await _context.SaveChangesAsync();

        var summary = await _sut.GetUserPhotosAsync(userId: 1);

        Assert.Equal(2, summary.Photos[0].Id); // DisplayOrder 1
        Assert.Equal(3, summary.Photos[1].Id); // DisplayOrder 2
        Assert.Equal(1, summary.Photos[2].Id); // DisplayOrder 3
    }

    [Fact]
    public async Task GetUserPhotos_NoPhotos_ReturnsEmptySummary()
    {
        var summary = await _sut.GetUserPhotosAsync(userId: 999);

        Assert.Equal(0, summary.TotalPhotos);
        Assert.Empty(summary.Photos);
        Assert.False(summary.HasPrimaryPhoto);
        Assert.Null(summary.PrimaryPhoto);
    }

    // ═══════════════════════════════════════════════════════
    //  GetPhotoAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task GetPhoto_ValidOwner_ReturnsPhoto()
    {
        var photo = CreatePhoto(userId: 1, id: 1);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.GetPhotoAsync(photoId: 1, userId: 1);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task GetPhoto_WrongUser_ReturnsNull()
    {
        var photo = CreatePhoto(userId: 1, id: 1);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.GetPhotoAsync(photoId: 1, userId: 99);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPhoto_DeletedPhoto_ReturnsNull()
    {
        var photo = CreatePhoto(userId: 1, id: 1, isDeleted: true);
        _context.Photos.Add(photo);
        await _context.SaveChangesAsync();

        var result = await _sut.GetPhotoAsync(photoId: 1, userId: 1);
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════
    //  CanUserUploadMorePhotosAsync
    // ═══════════════════════════════════════════════════════

    [Fact]
    public async Task CanUserUploadMore_UnderLimit_ReturnsTrue()
    {
        // Add 5 photos (limit is 6)
        for (int i = 1; i <= 5; i++)
            _context.Photos.Add(CreatePhoto(userId: 1, id: i));
        await _context.SaveChangesAsync();

        var result = await _sut.CanUserUploadMorePhotosAsync(userId: 1);
        Assert.True(result);
    }

    [Fact]
    public async Task CanUserUploadMore_AtLimit_ReturnsFalse()
    {
        // Add exactly 6 photos (MaxPhotosPerUser = 6)
        for (int i = 1; i <= 6; i++)
            _context.Photos.Add(CreatePhoto(userId: 1, id: i));
        await _context.SaveChangesAsync();

        var result = await _sut.CanUserUploadMorePhotosAsync(userId: 1);
        Assert.False(result);
    }

    [Fact]
    public async Task CanUserUploadMore_DeletedPhotosNotCounted()
    {
        // 5 active + 3 deleted = only 5 count
        for (int i = 1; i <= 5; i++)
            _context.Photos.Add(CreatePhoto(userId: 1, id: i));
        for (int i = 6; i <= 8; i++)
            _context.Photos.Add(CreatePhoto(userId: 1, id: i, isDeleted: true));
        await _context.SaveChangesAsync();

        var result = await _sut.CanUserUploadMorePhotosAsync(userId: 1);
        Assert.True(result);
    }

    [Fact]
    public async Task CanUserUploadMore_NoPhotos_ReturnsTrue()
    {
        var result = await _sut.CanUserUploadMorePhotosAsync(userId: 1);
        Assert.True(result);
    }
}
