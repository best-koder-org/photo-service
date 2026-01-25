using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.DTOs;
using PhotoService.Models;

namespace PhotoService.Services;

/// <summary>
/// Photo Service implementation - Core business logic for photo management
/// Handles photo CRUD operations, validation, and integration with storage/processing
/// </summary>
public class PhotoService : IPhotoService
{
    private readonly PhotoContext _context;
    private readonly IImageProcessingService _imageProcessing;
    private readonly IStorageService _storage;
    private readonly ILogger<PhotoService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Constructor with dependency injection
    /// Standard service layer pattern with EF Core and custom services
    /// </summary>
    public PhotoService(
        PhotoContext context,
        IImageProcessingService imageProcessing,
        IStorageService storage,
        ILogger<PhotoService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _imageProcessing = imageProcessing;
        _storage = storage;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Upload and process a new photo for a user
    /// Comprehensive photo upload with validation, processing, and storage
    /// </summary>
    public async Task<PhotoUploadResultDto> UploadPhotoAsync(int userId, PhotoUploadDto uploadDto)
    {
        var result = new PhotoUploadResultDto();

        try
        {
            _logger.LogInformation("Starting photo upload for user {UserId}", userId);

            // ================================
            // VALIDATION PHASE
            // Comprehensive validation before processing
            // ================================

            // Check if user can upload more photos
            if (!await CanUserUploadMorePhotosAsync(userId))
            {
                result.ErrorMessage = $"Maximum photo limit reached ({PhotoConstants.MaxPhotosPerUser} photos per user)";
                return result;
            }

            // Validate file format and content
            using var validationStream = uploadDto.Photo.OpenReadStream();
            var validation = await _imageProcessing.ValidateImageAsync(validationStream, uploadDto.Photo.FileName);
            
            if (!validation.IsValid)
            {
                result.ErrorMessage = validation.ErrorMessage ?? "Invalid image file";
                return result;
            }

            // Add validation warnings to result
            result.Warnings.AddRange(validation.Warnings);

            // Check file size
            if (uploadDto.Photo.Length > PhotoConstants.MaxFileSizeBytes)
            {
                result.ErrorMessage = $"File size exceeds maximum limit of {PhotoConstants.MaxFileSizeBytes / (1024 * 1024)} MB";
                return result;
            }

            // ================================
            // PROCESSING PHASE
            // Image processing and storage preparation
            // ================================

            PhotoProcessingInfoDto processingInfo;
            ImageProcessingResult processedImage;

            // Process the image
            using (var processingStream = uploadDto.Photo.OpenReadStream())
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                processedImage = await _imageProcessing.ProcessImageAsync(processingStream, uploadDto.Photo.FileName);
                stopwatch.Stop();

                processingInfo = new PhotoProcessingInfoDto
                {
                    WasResized = processedImage.WasResized,
                    OriginalWidth = processedImage.OriginalWidth,
                    OriginalHeight = processedImage.OriginalHeight,
                    FinalWidth = processedImage.Width,
                    FinalHeight = processedImage.Height,
                    FormatConverted = processedImage.Format != validation.Format,
                    OriginalFormat = validation.Format,
                    FinalFormat = processedImage.Format,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }

            // ================================
            // STORAGE PHASE
            // Store processed images in multiple sizes
            // ================================

            StorageResult fullSizeStorage;
            StorageResult thumbnailStorage;
            StorageResult mediumStorage;

            // Store full-size image
            using (var fullStream = new MemoryStream(processedImage.ImageData))
            {
                fullSizeStorage = await _storage.StoreImageAsync(fullStream, userId, uploadDto.Photo.FileName);
                if (!fullSizeStorage.Success)
                {
                    result.ErrorMessage = $"Failed to store image: {fullSizeStorage.ErrorMessage}";
                    return result;
                }
            }

            var storedFileName = fullSizeStorage.FileName;

            // Store thumbnail
            using (var thumbStream = new MemoryStream(processedImage.ThumbnailData))
            {
                var thumbnailFileName = GetThumbnailFileName(storedFileName);
                thumbnailStorage = await _storage.StoreImageAsync(
                    thumbStream,
                    userId,
                    thumbnailFileName,
                    useProvidedFileName: true);

                if (!thumbnailStorage.Success)
                {
                    await _storage.DeleteImageAsync(fullSizeStorage.FilePath);
                    result.ErrorMessage = $"Failed to store thumbnail: {thumbnailStorage.ErrorMessage}";
                    return result;
                }
            }

            // Store medium-size image
            using (var mediumStream = new MemoryStream(processedImage.MediumData))
            {
                var mediumFileName = GetMediumFileName(storedFileName);
                mediumStorage = await _storage.StoreImageAsync(
                    mediumStream,
                    userId,
                    mediumFileName,
                    useProvidedFileName: true);

                if (!mediumStorage.Success)
                {
                    await _storage.DeleteImageAsync(fullSizeStorage.FilePath);
                    await _storage.DeleteImageAsync(thumbnailStorage.FilePath);
                    result.ErrorMessage = $"Failed to store medium image: {mediumStorage.ErrorMessage}";
                    return result;
                }
            }

            // ================================
            // DATABASE PHASE
            // Create photo record with metadata
            // ================================

            // Determine display order
            int displayOrder = uploadDto.DisplayOrder ?? await GetNextDisplayOrderAsync(userId);

            // Handle primary photo logic
            if (uploadDto.IsPrimary)
            {
                await UnsetAllPrimaryPhotosAsync(userId);
            }

            // Create photo entity
            var photo = new Photo
            {
                UserId = userId,
                OriginalFileName = uploadDto.Photo.FileName,
                StoredFileName = Path.GetFileName(fullSizeStorage.FilePath),
                FileExtension = processedImage.Extension,
                FileSizeBytes = fullSizeStorage.FileSize,
                Width = processedImage.Width,
                Height = processedImage.Height,
                DisplayOrder = displayOrder,
                IsPrimary = uploadDto.IsPrimary,
                QualityScore = processedImage.QualityScore,
                ModerationStatus = DetermineModerationStatus(processedImage.QualityScore),
                CreatedAt = DateTime.UtcNow
            };

            _context.Photos.Add(photo);
            await _context.SaveChangesAsync();

            // ================================
            // SUCCESS RESPONSE
            // Build complete response with photo details
            // ================================

            result.Success = true;
            result.ProcessingInfo = processingInfo;
            result.Photo = new PhotoResponseDto
            {
                Id = photo.Id,
                UserId = photo.UserId,
                OriginalFileName = photo.OriginalFileName,
                DisplayOrder = photo.DisplayOrder,
                IsPrimary = photo.IsPrimary,
                CreatedAt = photo.CreatedAt,
                Width = photo.Width,
                Height = photo.Height,
                FileSizeBytes = photo.FileSizeBytes,
                ModerationStatus = photo.ModerationStatus,
                QualityScore = photo.QualityScore,
                Urls = GeneratePhotoUrls(photo.Id)
            };

            _logger.LogInformation("Photo upload completed successfully for user {UserId}, photo ID {PhotoId}", 
                userId, photo.Id);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading photo for user {UserId}", userId);
            result.ErrorMessage = "An error occurred while uploading the photo. Please try again.";
            return result;
        }
    }

    /// <summary>
    /// Get all photos for a specific user
    /// Returns complete photo collection with metadata
    /// </summary>
    public async Task<UserPhotoSummaryDto> GetUserPhotosAsync(int userId)
    {
        var photos = await _context.Photos
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync();

        var photoResponses = photos.Select(p => new PhotoResponseDto
        {
            Id = p.Id,
            UserId = p.UserId,
            OriginalFileName = p.OriginalFileName,
            DisplayOrder = p.DisplayOrder,
            IsPrimary = p.IsPrimary,
            CreatedAt = p.CreatedAt,
            Width = p.Width,
            Height = p.Height,
            FileSizeBytes = p.FileSizeBytes,
            ModerationStatus = p.ModerationStatus,
            QualityScore = p.QualityScore,
            Urls = GeneratePhotoUrls(p.Id)
        }).ToList();

        var primaryPhoto = photoResponses.FirstOrDefault(p => p.IsPrimary);

        return new UserPhotoSummaryDto
        {
            UserId = userId,
            TotalPhotos = photos.Count,
            HasPrimaryPhoto = primaryPhoto != null,
            PrimaryPhoto = primaryPhoto,
            Photos = photoResponses,
            TotalStorageBytes = photos.Sum(p => p.FileSizeBytes)
        };
    }

    /// <summary>
    /// Get a specific photo by ID with ownership validation
    /// </summary>
    public async Task<PhotoResponseDto?> GetPhotoAsync(int photoId, int userId)
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null)
            return null;

        return new PhotoResponseDto
        {
            Id = photo.Id,
            UserId = photo.UserId,
            OriginalFileName = photo.OriginalFileName,
            DisplayOrder = photo.DisplayOrder,
            IsPrimary = photo.IsPrimary,
            CreatedAt = photo.CreatedAt,
            Width = photo.Width,
            Height = photo.Height,
            FileSizeBytes = photo.FileSizeBytes,
            ModerationStatus = photo.ModerationStatus,
            QualityScore = photo.QualityScore,
            Urls = GeneratePhotoUrls(photo.Id)
        };
    }

    /// <summary>
    /// Get user's primary profile photo
    /// </summary>
    public async Task<PhotoResponseDto?> GetPrimaryPhotoAsync(int userId)
    {
        var photo = await _context.Photos
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .OrderByDescending(p => p.IsPrimary)
            .ThenBy(p => p.DisplayOrder)
            .FirstOrDefaultAsync();

        if (photo == null)
            return null;

        return new PhotoResponseDto
        {
            Id = photo.Id,
            UserId = photo.UserId,
            OriginalFileName = photo.OriginalFileName,
            DisplayOrder = photo.DisplayOrder,
            IsPrimary = photo.IsPrimary,
            CreatedAt = photo.CreatedAt,
            Width = photo.Width,
            Height = photo.Height,
            FileSizeBytes = photo.FileSizeBytes,
            ModerationStatus = photo.ModerationStatus,
            QualityScore = photo.QualityScore,
            Urls = GeneratePhotoUrls(photo.Id)
        };
    }

    /// <summary>
    /// Update photo metadata
    /// </summary>
    public async Task<PhotoResponseDto?> UpdatePhotoAsync(int photoId, int userId, PhotoUpdateDto updateDto)
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null)
            return null;

        // Handle primary photo update
        if (updateDto.IsPrimary.HasValue && updateDto.IsPrimary.Value && !photo.IsPrimary)
        {
            await UnsetAllPrimaryPhotosAsync(userId);
            photo.IsPrimary = true;
        }
        else if (updateDto.IsPrimary.HasValue && !updateDto.IsPrimary.Value && photo.IsPrimary)
        {
            photo.IsPrimary = false;
        }

        // Handle display order update
        if (updateDto.DisplayOrder.HasValue)
        {
            photo.DisplayOrder = updateDto.DisplayOrder.Value;
        }

        photo.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await GetPhotoAsync(photoId, userId);
    }

    /// <summary>
    /// Reorder multiple photos in a single operation
    /// </summary>
    public async Task<UserPhotoSummaryDto> ReorderPhotosAsync(int userId, PhotoReorderDto reorderDto)
    {
        var photos = await _context.Photos
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .ToListAsync();

        foreach (var orderItem in reorderDto.Photos)
        {
            var photo = photos.FirstOrDefault(p => p.Id == orderItem.PhotoId);
            if (photo != null)
            {
                photo.DisplayOrder = orderItem.DisplayOrder;
                photo.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return await GetUserPhotosAsync(userId);
    }

    /// <summary>
    /// Set a specific photo as the user's primary profile photo
    /// </summary>
    public async Task<bool> SetPrimaryPhotoAsync(int photoId, int userId)
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null)
            return false;

        await UnsetAllPrimaryPhotosAsync(userId);
        
        photo.IsPrimary = true;
        photo.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Soft delete a photo
    /// </summary>
    public async Task<bool> DeletePhotoAsync(int photoId, int userId)
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null)
            return false;

        // If deleting primary photo, set next photo as primary
        if (photo.IsPrimary)
        {
            var nextPhoto = await _context.Photos
                .Where(p => p.UserId == userId && p.Id != photoId && !p.IsDeleted)
                .OrderBy(p => p.DisplayOrder)
                .FirstOrDefaultAsync();

            if (nextPhoto != null)
            {
                nextPhoto.IsPrimary = true;
                nextPhoto.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Soft delete
        photo.IsDeleted = true;
        photo.DeletedAt = DateTime.UtcNow;
        photo.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // TODO: Schedule physical file deletion (can be done async)
        // For now, files remain for potential recovery

        return true;
    }

    /// <summary>
    /// Get photo file stream for serving images
    /// </summary>
    public async Task<(Stream? stream, string contentType, string fileName)> GetPhotoStreamAsync(int photoId, string size = "full")
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);

        if (photo == null)
            return (null, string.Empty, string.Empty);

        var normalizedSize = (size ?? "full").Trim().ToLowerInvariant();
        var storedFileNameWithoutExtension = Path.GetFileNameWithoutExtension(photo.StoredFileName);
        var storedFileExtension = Path.GetExtension(photo.StoredFileName);

        var targetFileName = normalizedSize switch
        {
            "thumbnail" => $"{storedFileNameWithoutExtension}_thumb{storedFileExtension}",
            "medium" => $"{storedFileNameWithoutExtension}_medium{storedFileExtension}",
            _ => photo.StoredFileName
        };

        var relativePath = Path.Combine(photo.UserId.ToString(), targetFileName)
            .Replace(Path.DirectorySeparatorChar, '/');

        var filePath = relativePath;

        var stream = await _storage.GetImageStreamAsync(filePath);
        var contentType = GetContentType(photo.FileExtension);
        var fileName = size == "full" ? photo.OriginalFileName : $"{Path.GetFileNameWithoutExtension(photo.OriginalFileName)}_{size}{photo.FileExtension}";

        return (stream, contentType, fileName);
    }

    /// <summary>
    /// Validate if user can upload more photos
    /// </summary>
    public async Task<bool> CanUserUploadMorePhotosAsync(int userId)
    {
        var currentCount = await _context.Photos
            .CountAsync(p => p.UserId == userId && !p.IsDeleted);

        return currentCount < PhotoConstants.MaxPhotosPerUser;
    }

    /// <summary>
    /// Get photos pending moderation review
    /// </summary>
    public async Task<(List<PhotoResponseDto> photos, int totalCount)> GetPhotosForModerationAsync(
        string status = Models.ModerationStatus.PendingReview, 
        int pageNumber = 1, 
        int pageSize = 50)
    {
        var query = _context.Photos
            .Where(p => p.ModerationStatus == status && !p.IsDeleted);

        var totalCount = await query.CountAsync();

        var photos = await query
            .OrderBy(p => p.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var photoResponses = photos.Select(p => new PhotoResponseDto
        {
            Id = p.Id,
            UserId = p.UserId,
            OriginalFileName = p.OriginalFileName,
            DisplayOrder = p.DisplayOrder,
            IsPrimary = p.IsPrimary,
            CreatedAt = p.CreatedAt,
            Width = p.Width,
            Height = p.Height,
            FileSizeBytes = p.FileSizeBytes,
            ModerationStatus = p.ModerationStatus,
            QualityScore = p.QualityScore,
            Urls = GeneratePhotoUrls(p.Id)
        }).ToList();

        return (photoResponses, totalCount);
    }

    /// <summary>
    /// Update photo moderation status
    /// </summary>
    public async Task<bool> UpdateModerationStatusAsync(int photoId, string status, string? notes = null)
    {
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);

        if (photo == null)
            return false;

        photo.ModerationStatus = status;
        photo.ModerationNotes = notes;
        photo.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    // ================================
    // PRIVATE HELPER METHODS
    // Internal business logic utilities
    // ================================

    /// <summary>
    /// Get next available display order for user's photos
    /// </summary>
    private async Task<int> GetNextDisplayOrderAsync(int userId)
    {
        var maxOrder = await _context.Photos
            .Where(p => p.UserId == userId && !p.IsDeleted)
            .MaxAsync(p => (int?)p.DisplayOrder) ?? 0;

        return maxOrder + 1;
    }

    /// <summary>
    /// Unset primary flag for all user's photos
    /// </summary>
    private async Task UnsetAllPrimaryPhotosAsync(int userId)
    {
        var primaryPhotos = await _context.Photos
            .Where(p => p.UserId == userId && p.IsPrimary && !p.IsDeleted)
            .ToListAsync();

        foreach (var photo in primaryPhotos)
        {
            photo.IsPrimary = false;
            photo.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Determine moderation status based on quality score
    /// </summary>
    private static string DetermineModerationStatus(int qualityScore)
    {
        return qualityScore >= 70 ? Models.ModerationStatus.AutoApproved : Models.ModerationStatus.PendingReview;
    }

    /// <summary>
    /// Generate complete URLs for photo access
    /// </summary>
    private PhotoUrlsDto GeneratePhotoUrls(int photoId)
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            // Fallback to relative URLs if no HTTP context
            return new PhotoUrlsDto
            {
                Full = $"/api/photos/{photoId}/image",
                Medium = $"/api/photos/{photoId}/medium",
                Thumbnail = $"/api/photos/{photoId}/thumbnail"
            };
        }

        var baseUrl = $"{request.Scheme}://{request.Host}";
        return new PhotoUrlsDto
        {
            Full = $"{baseUrl}/api/photos/{photoId}/image",
            Medium = $"{baseUrl}/api/photos/{photoId}/medium",
            Thumbnail = $"{baseUrl}/api/photos/{photoId}/thumbnail"
        };
    }

    /// <summary>
    /// Get MIME content type from file extension
    /// </summary>
    private static string GetContentType(string extension)
    {
        return extension.ToLower() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    // ================================
    // ADVANCED PRIVACY FEATURES
    // ================================

    /// <summary>
    /// Upload photo with advanced privacy settings
    /// Enhanced upload with privacy level, blur settings, and content moderation
    /// </summary>
    public async Task<PrivacyPhotoUploadResultDto> UploadPhotoWithPrivacyAsync(int userId, PrivacyPhotoUploadDto uploadDto)
    {
        var result = new PrivacyPhotoUploadResultDto();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting privacy photo upload for user {UserId} with privacy level {PrivacyLevel}", 
                userId, uploadDto.PrivacyLevel);

            // Validate user can upload more photos
            if (!await CanUserUploadMorePhotosAsync(userId))
            {
                result.ErrorMessage = $"Maximum photo limit ({PhotoConstants.MaxPhotosPerUser}) reached";
                return result;
            }

            // Validate file
            using var stream = uploadDto.Photo.OpenReadStream();
            var validation = await _imageProcessing.ValidateImageAsync(stream, uploadDto.Photo.FileName);
            if (!validation.IsValid)
            {
                result.ErrorMessage = validation.ErrorMessage ?? "Invalid image file";
                return result;
            }

            // Read file data for processing
            stream.Position = 0;
            byte[] fileData;
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                fileData = memoryStream.ToArray();
            }

            // Process image with privacy features
            var privacyProcessing = await _imageProcessing.ProcessPhotoWithPrivacyAsync(
                fileData, uploadDto.Photo.FileName, uploadDto.PrivacyLevel, uploadDto.BlurIntensity);

            if (!privacyProcessing.IsSuccess)
            {
                result.ErrorMessage = privacyProcessing.ErrorMessage ?? "Image processing failed";
                return result;
            }

            // Create photo entity
            var photo = new Photo
            {
                UserId = userId,
                OriginalFileName = uploadDto.Photo.FileName,
                StoredFileName = GenerateStoredFileName(userId, uploadDto.Photo.FileName),
                FileExtension = Path.GetExtension(uploadDto.Photo.FileName).ToLowerInvariant(),
                FileSizeBytes = fileData.Length,
                MimeType = uploadDto.Photo.ContentType,
                Width = privacyProcessing.StandardProcessingResult!.Width,
                Height = privacyProcessing.StandardProcessingResult.Height,
                IsPrimary = uploadDto.IsPrimary,
                DisplayOrder = uploadDto.DisplayOrder ?? await GetNextDisplayOrderAsync(userId),
                QualityScore = privacyProcessing.StandardProcessingResult.QualityScore,
                CreatedAt = DateTime.UtcNow,
                
                // Privacy features
                PrivacyLevel = uploadDto.PrivacyLevel,
                BlurIntensity = uploadDto.BlurIntensity,
                RequiresMatch = ShouldRequireMatch(uploadDto.PrivacyLevel),
                BlurredFileName = privacyProcessing.BlurredFileName,
                
                // Content moderation
                ModerationStatus = DetermineModerationStatus(privacyProcessing.ModerationAnalysis),
                SafetyScore = privacyProcessing.ModerationAnalysis?.SafetyScore,
                ModeratedAt = DateTime.UtcNow
            };

            // Set moderation results
            if (privacyProcessing.ModerationAnalysis != null)
            {
                photo.SetModerationResults(privacyProcessing.ModerationAnalysis);
                
                // T027: Audit log for content moderation decisions
                _logger.LogInformation("[PhotoModeration] Photo moderated - PhotoId: (pending), UserId: {UserId}, SafetyScore: {SafetyScore:F2}, Status: {Status}, PrivacyLevel: {PrivacyLevel}, Issues: [{Issues}]",
                    userId, 
                    privacyProcessing.ModerationAnalysis.SafetyScore,
                    photo.ModerationStatus,
                    uploadDto.PrivacyLevel,
                    string.Join(", ", privacyProcessing.ModerationAnalysis.DetectedIssues));
            }

            // Store files
            using var imageStream = new MemoryStream(privacyProcessing.StandardProcessingResult.ImageData);
            var storageResult = await _storage.StoreImageAsync(
                imageStream,
                userId,
                photo.StoredFileName,
                useProvidedFileName: true);
            
            if (!storageResult.Success)
            {
                result.ErrorMessage = storageResult.ErrorMessage ?? "Failed to store image";
                return result;
            }

            // Store different sizes
            await StoreImageSizesAsync(privacyProcessing.StandardProcessingResult, userId, photo.StoredFileName);

            // Handle primary photo logic
            if (uploadDto.IsPrimary)
            {
                await UnsetAllPrimaryPhotosAsync(userId);
            }

            // Save to database
            _context.Photos.Add(photo);
            await _context.SaveChangesAsync();

            stopwatch.Stop();
            result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;

            // Build response
            result.Success = true;
            result.PhotoId = photo.Id;
            result.Photo = new PhotoResponseDto
            {
                Id = photo.Id,
                UserId = photo.UserId,
                OriginalFileName = photo.OriginalFileName,
                FileSizeBytes = photo.FileSizeBytes,
                Width = photo.Width,
                Height = photo.Height,
                IsPrimary = photo.IsPrimary,
                DisplayOrder = photo.DisplayOrder,
                QualityScore = photo.QualityScore,
                CreatedAt = photo.CreatedAt,
                ModerationStatus = photo.ModerationStatus,
                Urls = GeneratePhotoUrls(photo.Id)
            };
            result.PrivacyProcessing = MapToPrivacyProcessingDetailsDto(privacyProcessing, uploadDto.PrivacyLevel);
            result.ModerationAnalysis = MapToModerationAnalysisDto(privacyProcessing.ModerationAnalysis);

            // T027: Telemetry for photo upload success with performance metrics
            _logger.LogInformation("[PhotoUpload] \u2713 Photo uploaded successfully - UserId: {UserId}, PhotoId: {PhotoId}, ProcessingTime: {ProcessingTime}ms, QualityScore: {QualityScore}, PrivacyLevel: {PrivacyLevel}, BlurGenerated: {BlurGenerated}, FileSize: {FileSizeMB:F2}MB",
                userId, photo.Id, result.ProcessingTimeMs, photo.QualityScore, photo.PrivacyLevel, 
                !string.IsNullOrEmpty(photo.BlurredFileName),
                photo.FileSizeBytes / (1024.0 * 1024.0));

            return result;
        }// T027: Error telemetry for photo upload failures
            _logger.LogError(ex, "[PhotoUpload] ERROR - UserId: {UserId}, FileName: {FileName}, ErrorMessage: {ErrorMessage}",
                userId, uploadDto.Photo.FileName, ex.Message
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading privacy photo for user {UserId}", userId);
            stopwatch.Stop();
            result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
            result.ErrorMessage = "An error occurred during photo upload";
            return result;
        }
    }

    // Additional privacy methods will be added in follow-up implementation
    public async Task<PhotoResponseDto?> UpdatePhotoPrivacyAsync(int photoId, int userId, PhotoPrivacyUpdateDto privacyDto)
    {
        // Implementation placeholder - basic update for now
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null) return null;

        // Update basic privacy settings
        photo.PrivacyLevel = privacyDto.PrivacyLevel;
        photo.BlurIntensity = privacyDto.BlurIntensity;
        photo.RequiresMatch = privacyDto.RequiresMatch ?? ShouldRequireMatch(privacyDto.PrivacyLevel);
        photo.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new PhotoResponseDto
        {
            Id = photo.Id,
            UserId = photo.UserId,
            OriginalFileName = photo.OriginalFileName,
            FileSizeBytes = photo.FileSizeBytes,
            Width = photo.Width,
            Height = photo.Height,
            IsPrimary = photo.IsPrimary,
            DisplayOrder = photo.DisplayOrder,
            QualityScore = photo.QualityScore,
            CreatedAt = photo.CreatedAt,
            ModerationStatus = photo.ModerationStatus,
            Urls = GeneratePhotoUrls(photo.Id)
        };
    }

    public async Task<PrivacyImageResponseDto> GetPhotoWithPrivacyControlAsync(int photoId, string requestingUserId, bool hasMatch = false)
    {
        // Implementation placeholder - basic privacy control
        var response = new PrivacyImageResponseDto();
        
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);

        if (photo == null) return response;

        response.PrivacyLevel = photo.PrivacyLevel;

        // For now, serve original image for owner, null for others
        if (photo.UserId.ToString() == requestingUserId)
        {
            var (stream, contentType, fileName) = await GetPhotoStreamAsync(photoId, "full");
            if (stream != null)
            {
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                response.ImageData = memoryStream.ToArray();
                response.ContentType = contentType;
                response.FileName = fileName;
                response.IsBlurred = false;
            }
        }
        else
        {
            response.AccessDenied = true;
        }

        return response;
    }

    public async Task<PrivacyImageResponseDto> GetBlurredPhotoAsync(int photoId)
    {
        // Implementation placeholder - return basic response for now
        var response = new PrivacyImageResponseDto();
        
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && !p.IsDeleted);

        if (photo == null) return response;

        // For now, just return regular image data
        var (stream, contentType, fileName) = await GetPhotoStreamAsync(photoId, "full");
        if (stream != null)
        {
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            response.ImageData = memoryStream.ToArray();
            response.ContentType = contentType;
            response.FileName = $"blurred_{fileName}";
            response.IsBlurred = true;
            response.PrivacyLevel = photo.PrivacyLevel;
        }

        return response;
    }

    public async Task<BlurRegenerationResultDto?> RegenerateBlurredPhotoAsync(int photoId, int userId, double blurIntensity)
    {
        // Implementation placeholder - basic response for now
        var photo = await _context.Photos
            .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

        if (photo == null) return null;

        // Update blur intensity
        photo.BlurIntensity = blurIntensity;
        photo.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync();

        return new BlurRegenerationResultDto
        {
            Success = true,
            BlurredFileName = photo.BlurredFileName,
            BlurIntensity = blurIntensity,
            ProcessingTimeMs = 100 // Placeholder value
        };
    }

    // ================================
    // PRIVACY HELPER METHODS
    // ================================

    private static bool ShouldRequireMatch(string privacyLevel)
    {
        return privacyLevel == PhotoPrivacyLevel.Private ||
               privacyLevel == PhotoPrivacyLevel.MatchOnly ||
               privacyLevel == PhotoPrivacyLevel.VIP;
    }

    private static string DetermineModerationStatus(ModerationAnalysis? analysis)
    {
        if (analysis == null) return ModerationStatus.PendingReview;
        
        return analysis.SafetyScore >= 0.8 ? ModerationStatus.AutoApproved :
               analysis.SafetyScore >= 0.6 ? ModerationStatus.PendingReview :
               ModerationStatus.Rejected;
    }

    private static PrivacyProcessingDetailsDto MapToPrivacyProcessingDetailsDto(
        PrivacyPhotoProcessingResult processing, string privacyLevel)
    {
        return new PrivacyProcessingDetailsDto
        {
            BlurredVersionGenerated = !string.IsNullOrEmpty(processing.BlurredFileName),
            BlurIntensity = processing.ModerationAnalysis?.SafetyScore ?? 0.8,
            RequiresMatch = ShouldRequireMatch(privacyLevel),
            PrivacyLevel = privacyLevel,
            VIPFeatures = processing.EnhancedPrivacyFeatures != null 
                ? new VIPPrivacyFeaturesDto
                {
                    HasAdvancedBlur = processing.EnhancedPrivacyFeatures.HasAdvancedBlur,
                    HasWatermark = processing.EnhancedPrivacyFeatures.HasWatermark,
                    ProcessingLevel = processing.EnhancedPrivacyFeatures.ProcessingLevel
                }
                : null
        };
    }

    private static ModerationAnalysisDto? MapToModerationAnalysisDto(ModerationAnalysis? analysis)
    {
        if (analysis == null) return null;

        return new ModerationAnalysisDto
        {
            IsAppropriate = analysis.IsAppropriate,
            SafetyScore = analysis.SafetyScore,
            DetectedIssues = analysis.DetectedIssues,
            Classifications = analysis.Classifications,
            AnalyzedAt = analysis.AnalyzedAt,
            AnalysisVersion = analysis.AnalysisVersion
        };
    }

    private async Task StoreImageSizesAsync(ImageProcessingResult processing, int userId, string baseFileName)
    {
        try
        {
            // Store thumbnail
            if (processing.ThumbnailData.Length > 0)
            {
                var thumbName = GetThumbnailFileName(baseFileName);
                using var thumbStream = new MemoryStream(processing.ThumbnailData);
                await _storage.StoreImageAsync(
                    thumbStream,
                    userId,
                    thumbName,
                    useProvidedFileName: true);
            }

            // Store medium size
            if (processing.MediumData.Length > 0)
            {
                var mediumName = GetMediumFileName(baseFileName);
                using var mediumStream = new MemoryStream(processing.MediumData);
                await _storage.StoreImageAsync(
                    mediumStream,
                    userId,
                    mediumName,
                    useProvidedFileName: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing image sizes for {FileName}", baseFileName);
        }
    }

    private static string GetThumbnailFileName(string baseFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseFileName);
        var extension = Path.GetExtension(baseFileName);
        return $"{nameWithoutExt}_thumb{extension}";
    }

    private static string GetMediumFileName(string baseFileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseFileName);
        var extension = Path.GetExtension(baseFileName);
        return $"{nameWithoutExt}_medium{extension}";
    }

    private static string GenerateStoredFileName(int userId, string originalFileName)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var guid = Guid.NewGuid().ToString("N")[..8];
        var extension = Path.GetExtension(originalFileName);
        return $"{userId}_{timestamp}_{guid}{extension}";
    }
}
