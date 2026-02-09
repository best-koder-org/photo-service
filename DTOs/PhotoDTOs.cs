using System.ComponentModel.DataAnnotations;

namespace PhotoService.DTOs;

/// <summary>
/// Data Transfer Object for photo upload requests
/// Validates file upload parameters and metadata
/// </summary>
public class PhotoUploadDto
{
    /// <summary>
    /// Uploaded photo file
    /// Required for photo upload operations
    /// </summary>
    [Required(ErrorMessage = "Photo file is required")]
    public IFormFile Photo { get; set; } = null!;

    /// <summary>
    /// Display order for the photo in user's gallery
    /// If not provided, will be set to next available order
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Display order must be a positive number")]
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Whether this should be set as the primary profile photo
    /// Only one photo per user can be primary
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// Optional description/caption for the photo
    /// Currently not stored but available for future use
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
}

/// <summary>
/// Data Transfer Object for photo response
/// Returns photo metadata and URLs to frontend
/// </summary>
public class PhotoResponseDto
{
    /// <summary>
    /// Unique photo identifier
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Owner user identifier
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Original filename as uploaded
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// Display order in user's photo gallery
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this is the user's primary profile photo
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Photo upload timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Image dimensions
    /// </summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Content moderation status
    /// </summary>
    public string ModerationStatus { get; set; } = string.Empty;

    /// <summary>
    /// Image quality score (1-100)
    /// </summary>
    public int QualityScore { get; set; }

    /// <summary>
    /// URLs for different image sizes
    /// Optimized for responsive frontend display
    /// </summary>
    public PhotoUrlsDto Urls { get; set; } = new();

    /// <summary>
    /// Helper property: Human-readable file size
    /// </summary>
    public string FileSizeFormatted => FormatFileSize(FileSizeBytes);

    /// <summary>
    /// Format file size in human-readable format
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int suffixIndex = 0;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {suffixes[suffixIndex]}";
    }
}

/// <summary>
/// Data Transfer Object for photo URLs
/// Provides different image sizes for responsive display
/// </summary>
public class PhotoUrlsDto
{
    /// <summary>
    /// Full-size image URL
    /// Original uploaded image (processed)
    /// </summary>
    public string Full { get; set; } = string.Empty;

    /// <summary>
    /// Medium-size image URL (400x400)
    /// Optimized for profile views and cards
    /// </summary>
    public string Medium { get; set; } = string.Empty;

    /// <summary>
    /// Thumbnail image URL (150x150)
    /// Optimized for list views and previews
    /// </summary>
    public string Thumbnail { get; set; } = string.Empty;
}

/// <summary>
/// Data Transfer Object for photo update requests
/// Allows updating photo metadata without re-uploading
/// </summary>
public class PhotoUpdateDto
{
    /// <summary>
    /// New display order for the photo
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Display order must be a positive number")]
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Whether this should be set as the primary profile photo
    /// Setting this to true will unset other photos as primary
    /// </summary>
    public bool? IsPrimary { get; set; }
}

/// <summary>
/// Data Transfer Object for bulk photo operations
/// Allows reordering multiple photos in single request
/// </summary>
public class PhotoReorderDto
{
    /// <summary>
    /// List of photo IDs with their new display orders
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one photo must be specified")]
    public List<PhotoOrderItemDto> Photos { get; set; } = new();
}

/// <summary>
/// Individual photo order item for bulk reordering
/// </summary>
public class PhotoOrderItemDto
{
    /// <summary>
    /// Photo identifier
    /// </summary>
    [Required]
    public int PhotoId { get; set; }

    /// <summary>
    /// New display order for this photo
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Display order must be a positive number")]
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Data Transfer Object for photo upload result
/// Returns success/failure information with photo details
/// </summary>
public class PhotoUploadResultDto
{
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Upload warnings (non-fatal issues)
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Photo information if upload successful
    /// </summary>
    public PhotoResponseDto? Photo { get; set; }

    /// <summary>
    /// Processing information
    /// </summary>
    public PhotoProcessingInfoDto? ProcessingInfo { get; set; }
}

/// <summary>
/// Data Transfer Object for photo processing information
/// Provides details about image processing operations
/// </summary>
public class PhotoProcessingInfoDto
{
    /// <summary>
    /// Whether image was resized during processing
    /// </summary>
    public bool WasResized { get; set; }

    /// <summary>
    /// Original image dimensions before processing
    /// </summary>
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }

    /// <summary>
    /// Final image dimensions after processing
    /// </summary>
    public int FinalWidth { get; set; }
    public int FinalHeight { get; set; }

    /// <summary>
    /// Whether image format was converted
    /// </summary>
    public bool FormatConverted { get; set; }

    /// <summary>
    /// Original file format
    /// </summary>
    public string OriginalFormat { get; set; } = string.Empty;

    /// <summary>
    /// Final file format after processing
    /// </summary>
    public string FinalFormat { get; set; } = string.Empty;

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// Data Transfer Object for user photo summary
/// Provides overview of user's photo collection
/// </summary>
public class UserPhotoSummaryDto
{
    /// <summary>
    /// User identifier
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Total number of active photos
    /// </summary>
    public int TotalPhotos { get; set; }

    /// <summary>
    /// Whether user has a primary photo set
    /// </summary>
    public bool HasPrimaryPhoto { get; set; }

    /// <summary>
    /// Primary photo information if available
    /// </summary>
    public PhotoResponseDto? PrimaryPhoto { get; set; }

    /// <summary>
    /// List of all user photos
    /// </summary>
    public List<PhotoResponseDto> Photos { get; set; } = new();

    /// <summary>
    /// Total storage used by user's photos (in bytes)
    /// </summary>
    public long TotalStorageBytes { get; set; }

    /// <summary>
    /// Number of additional photos user can upload
    /// </summary>
    public int RemainingPhotoSlots => Math.Max(0, PhotoService.Models.PhotoConstants.MaxPhotosPerUser - TotalPhotos);

    /// <summary>
    /// Whether user has reached photo limit
    /// </summary>
    public bool HasReachedPhotoLimit => TotalPhotos >= PhotoService.Models.PhotoConstants.MaxPhotosPerUser;
}

// ================================
// ADVANCED PRIVACY FEATURE DTOs
// ================================

/// <summary>
/// Data Transfer Object for privacy-enabled photo upload requests
/// Extends standard upload with privacy settings and blur configuration
/// </summary>
public class PrivacyPhotoUploadDto
{
    /// <summary>
    /// Uploaded photo file
    /// Required for photo upload operations
    /// </summary>
    [Required(ErrorMessage = "Photo file is required")]
    public IFormFile Photo { get; set; } = null!;

    /// <summary>
    /// Privacy level for the photo
    /// Controls visibility and blur requirements
    /// </summary>
    [Required(ErrorMessage = "Privacy level is required")]
    [MaxLength(20, ErrorMessage = "Privacy level cannot exceed 20 characters")]
    public string PrivacyLevel { get; set; } = PhotoService.Models.PhotoPrivacyLevel.Public;

    /// <summary>
    /// Blur intensity for private photos (0.0 to 1.0)
    /// Higher values create more intense blur effects
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Blur intensity must be between 0.0 and 1.0")]
    public double BlurIntensity { get; set; } = 0.8;

    /// <summary>
    /// Display order for the photo in user's gallery
    /// If not provided, will be set to next available order
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Display order must be a positive number")]
    public int? DisplayOrder { get; set; }

    /// <summary>
    /// Whether this should be set as the primary profile photo
    /// Only one photo per user can be primary
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// Optional description/caption for the photo
    /// Currently not stored but available for future use
    /// </summary>
    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
}

/// <summary>
/// Data Transfer Object for updating photo privacy settings
/// Allows changing privacy level and blur configuration
/// </summary>
public class PhotoPrivacyUpdateDto
{
    /// <summary>
    /// New privacy level for the photo
    /// </summary>
    [Required(ErrorMessage = "Privacy level is required")]
    [MaxLength(20, ErrorMessage = "Privacy level cannot exceed 20 characters")]
    public string PrivacyLevel { get; set; } = PhotoService.Models.PhotoPrivacyLevel.Public;

    /// <summary>
    /// New blur intensity (0.0 to 1.0)
    /// Only applied if privacy level requires blurring
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "Blur intensity must be between 0.0 and 1.0")]
    public double BlurIntensity { get; set; } = 0.8;

    /// <summary>
    /// Whether photo requires a match to view in full resolution
    /// Automatically set based on privacy level, but can be overridden
    /// </summary>
    public bool? RequiresMatch { get; set; }
}

/// <summary>
/// Data Transfer Object for privacy-enabled photo upload results
/// Extends standard upload result with privacy processing information
/// </summary>
public class PrivacyPhotoUploadResultDto
{
    /// <summary>
    /// Whether upload operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Unique identifier of uploaded photo
    /// </summary>
    public int PhotoId { get; set; }

    /// <summary>
    /// Standard photo response data
    /// </summary>
    public PhotoResponseDto? Photo { get; set; }

    /// <summary>
    /// Privacy processing details
    /// </summary>
    public PrivacyProcessingDetailsDto? PrivacyProcessing { get; set; }

    /// <summary>
    /// Content moderation analysis results
    /// </summary>
    public ModerationAnalysisDto? ModerationAnalysis { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Total processing time in milliseconds
    /// </summary>
    public int ProcessingTimeMs { get; set; }
}

/// <summary>
/// Data Transfer Object for privacy processing details
/// Contains information about blur generation and privacy features
/// </summary>
public class PrivacyProcessingDetailsDto
{
    /// <summary>
    /// Whether a blurred version was generated
    /// </summary>
    public bool BlurredVersionGenerated { get; set; }

    /// <summary>
    /// Blur intensity applied
    /// </summary>
    public double BlurIntensity { get; set; }

    /// <summary>
    /// Whether photo requires match for full visibility
    /// </summary>
    public bool RequiresMatch { get; set; }

    /// <summary>
    /// Privacy level applied
    /// </summary>
    public string PrivacyLevel { get; set; } = string.Empty;

    /// <summary>
    /// VIP features applied (if applicable)
    /// </summary>
    public VIPPrivacyFeaturesDto? VIPFeatures { get; set; }
}

/// <summary>
/// Data Transfer Object for VIP privacy features
/// Premium privacy controls and enhancements
/// </summary>
public class VIPPrivacyFeaturesDto
{
    /// <summary>
    /// Whether advanced blur patterns were applied
    /// </summary>
    public bool HasAdvancedBlur { get; set; }

    /// <summary>
    /// Whether watermarking was applied
    /// </summary>
    public bool HasWatermark { get; set; }

    /// <summary>
    /// Processing level applied
    /// </summary>
    public string ProcessingLevel { get; set; } = string.Empty;
}

/// <summary>
/// Data Transfer Object for content moderation analysis results
/// Provides transparency about AI content analysis
/// </summary>
public class ModerationAnalysisDto
{
    /// <summary>
    /// Whether content is considered appropriate
    /// </summary>
    public bool IsAppropriate { get; set; }

    /// <summary>
    /// Overall safety score (0.0 to 1.0)
    /// </summary>
    public double SafetyScore { get; set; }

    /// <summary>
    /// Detected content issues (if any)
    /// </summary>
    public string[] DetectedIssues { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Detailed classification scores
    /// </summary>
    public Dictionary<string, double> Classifications { get; set; } = new();

    /// <summary>
    /// When analysis was performed
    /// </summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>
    /// Analysis version/algorithm used
    /// </summary>
    public string AnalysisVersion { get; set; } = "1.0";
}

/// <summary>
/// Data Transfer Object for privacy-controlled image responses
/// Indicates what type of image is being served
/// </summary>
public class PrivacyImageResponseDto
{
    /// <summary>
    /// Image data
    /// </summary>
    public byte[]? ImageData { get; set; }

    /// <summary>
    /// Content type for HTTP response
    /// </summary>
    public string ContentType { get; set; } = "image/jpeg";

    /// <summary>
    /// Filename for HTTP response
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a blurred version
    /// </summary>
    public bool IsBlurred { get; set; }

    /// <summary>
    /// Whether access was denied due to privacy settings
    /// </summary>
    public bool AccessDenied { get; set; }

    /// <summary>
    /// Privacy level of the original photo
    /// </summary>
    public string PrivacyLevel { get; set; } = string.Empty;
}

/// <summary>
/// Data Transfer Object for blur regeneration results
/// Provides feedback on blur processing operations
/// </summary>
public class BlurRegenerationResultDto
{
    /// <summary>
    /// Whether regeneration was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// New blur filename
    /// </summary>
    public string? BlurredFileName { get; set; }

    /// <summary>
    /// Blur intensity applied
    /// </summary>
    public double BlurIntensity { get; set; }

    /// <summary>
    /// Processing time in milliseconds
    /// </summary>
    public int ProcessingTimeMs { get; set; }

    /// <summary>
    /// Error message if regeneration failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Spec-aligned photo asset DTO.
/// Aggregates data from Photo model into the shape defined in api-spec.md:
///   { id, url, blurUrl, privacyLevel, moderationStatus, orderIndex }
/// </summary>
public class PhotoAssetDto
{
    /// <summary>Unique photo identifier</summary>
    public int Id { get; set; }

    /// <summary>URL for the display-sized photo (medium/responsive)</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Blurred/privacy-safe URL for non-matched users</summary>
    public string? BlurUrl { get; set; }

    /// <summary>Full-size photo URL (available after match or if public)</summary>
    public string? FullUrl { get; set; }

    /// <summary>Thumbnail URL for list views</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Privacy level: public, match-only, private</summary>
    public string PrivacyLevel { get; set; } = "public";

    /// <summary>Content moderation status: pending, approved, rejected</summary>
    public string ModerationStatus { get; set; } = "pending";

    /// <summary>Display order in user's photo gallery (1-based)</summary>
    public int OrderIndex { get; set; }

    /// <summary>Whether this is the primary profile photo</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Image dimensions</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Upload timestamp</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Extension to map Photo model → PhotoAssetDto (spec-aligned).
/// </summary>
public static class PhotoAssetMapper
{
    public static PhotoAssetDto ToAssetDto(this Models.Photo photo)
    {
        return new PhotoAssetDto
        {
            Id = photo.Id,
            Url = photo.Url,
            BlurUrl = string.IsNullOrEmpty(photo.BlurredFileName) ? null : photo.BlurredUrl,
            FullUrl = photo.Url,
            ThumbnailUrl = null, // TODO: wire up when thumbnail gen is ready
            PrivacyLevel = photo.PrivacyLevel,
            ModerationStatus = photo.ModerationStatus,
            OrderIndex = photo.DisplayOrder,
            IsPrimary = photo.IsPrimary,
            Width = photo.Width,
            Height = photo.Height,
            CreatedAt = photo.CreatedAt
        };
    }

    public static List<PhotoAssetDto> ToAssetDtos(this IEnumerable<Models.Photo> photos)
    {
        return photos.Select(p => p.ToAssetDto()).ToList();
    }
}
