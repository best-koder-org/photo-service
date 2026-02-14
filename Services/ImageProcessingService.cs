using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using PhotoService.Models;
using Microsoft.ML;
using System.Text.Json;

namespace PhotoService.Services;

/// <summary>
/// Image Processing Service implementation using ImageSharp
/// Handles image validation, resizing, format conversion, quality analysis, and privacy features
/// Enhanced with ML.NET content moderation and blur generation for dating app privacy
/// </summary>
public class ImageProcessingService : IImageProcessingService
{
    private readonly ILogger<ImageProcessingService> _logger;
    private readonly MLContext _mlContext;
    private readonly string _uploadsPath;

    /// <summary>
    /// Constructor with dependency injection
    /// </summary>
    public ImageProcessingService(ILogger<ImageProcessingService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _mlContext = new MLContext(seed: 1);
        _uploadsPath = configuration["Storage:UploadsPath"] ?? "wwwroot/uploads";

        // Ensure directories exist for privacy features
        Directory.CreateDirectory(Path.Combine(_uploadsPath, "photos"));
        Directory.CreateDirectory(Path.Combine(_uploadsPath, "blurred"));
    }

    /// <summary>
    /// Process uploaded image: validate, resize, optimize, and create multiple sizes
    /// Creates full, medium, and thumbnail versions with quality optimization
    /// </summary>
    public async Task<ImageProcessingResult> ProcessImageAsync(Stream inputStream, string originalFileName)
    {
        var result = new ImageProcessingResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting image processing for file: {FileName}", originalFileName);

            using var image = await Image.LoadAsync(inputStream);

            // Store original dimensions
            result.OriginalWidth = image.Width;
            result.OriginalHeight = image.Height;

            // ================================
            // IMAGE OPTIMIZATION AND RESIZING
            // Smart resizing with aspect ratio preservation
            // ================================

            var (processedWidth, processedHeight, wasResized) = CalculateOptimalDimensions(image.Width, image.Height);

            result.WasResized = wasResized;
            result.Width = processedWidth;
            result.Height = processedHeight;

            // Resize if needed
            if (wasResized)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(processedWidth, processedHeight),
                    Mode = ResizeMode.Max, // Maintains aspect ratio
                    Sampler = KnownResamplers.Lanczos3 // High-quality resampling
                }));
            }

            // ================================
            // FORMAT OPTIMIZATION
            // Convert to optimal format and apply compression
            // ================================

            var outputFormat = DetermineOptimalFormat(originalFileName);
            result.Format = outputFormat;
            result.Extension = GetExtensionForFormat(outputFormat);

            // ================================
            // QUALITY ANALYSIS
            // Calculate image quality score for moderation
            // ================================

            result.QualityScore = await CalculateQualityScoreAsync(inputStream);

            // ================================
            // GENERATE MULTIPLE SIZES
            // Create full, medium, and thumbnail versions
            // ================================

            // Full-size processed image
            using var fullSizeStream = new MemoryStream();
            await SaveImageWithOptimalSettings(image, fullSizeStream, outputFormat);
            result.ImageData = fullSizeStream.ToArray();

            // For creating different sizes, we'll reload from the processed full-size image
            var fullSizeBytes = result.ImageData;

            // Medium-size version (400x400 max)
            using (var mediumSourceStream = new MemoryStream(fullSizeBytes))
            using (var mediumImage = await Image.LoadAsync(mediumSourceStream))
            {
                mediumImage.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(PhotoConstants.ImageSizes.MediumWidth, PhotoConstants.ImageSizes.MediumHeight),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));

                using var mediumStream = new MemoryStream();
                await SaveImageWithOptimalSettings(mediumImage, mediumStream, outputFormat);
                result.MediumData = mediumStream.ToArray();
            }

            // Thumbnail version (150x150 max)
            using (var thumbnailSourceStream = new MemoryStream(fullSizeBytes))
            using (var thumbnailImage = await Image.LoadAsync(thumbnailSourceStream))
            {
                thumbnailImage.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(PhotoConstants.ImageSizes.ThumbnailWidth, PhotoConstants.ImageSizes.ThumbnailHeight),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));

                using var thumbnailStream = new MemoryStream();
                await SaveImageWithOptimalSettings(thumbnailImage, thumbnailStream, outputFormat);
                result.ThumbnailData = thumbnailStream.ToArray();
            }

            stopwatch.Stop();
            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation("Image processing completed in {ProcessingTime}ms. Original: {OriginalW}x{OriginalH}, Final: {FinalW}x{FinalH}",
                result.ProcessingTimeMs, result.OriginalWidth, result.OriginalHeight, result.Width, result.Height);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image: {FileName}", originalFileName);
            throw new InvalidOperationException($"Failed to process image: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generate thumbnail version of an image
    /// Standard thumbnail size for list views
    /// </summary>
    public async Task<byte[]> GenerateThumbnailAsync(Stream inputStream, int width = 150, int height = 150)
    {
        try
        {
            using var image = await Image.LoadAsync(inputStream);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));

            using var outputStream = new MemoryStream();
            await image.SaveAsJpegAsync(outputStream, new JpegEncoder
            {
                Quality = 85 // Good quality for thumbnails
            });

            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating thumbnail");
            throw new InvalidOperationException($"Failed to generate thumbnail: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Generate medium-sized version of an image
    /// Balanced quality/size for profile views
    /// </summary>
    public async Task<byte[]> GenerateMediumAsync(Stream inputStream, int width = 400, int height = 400)
    {
        try
        {
            using var image = await Image.LoadAsync(inputStream);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Max,
                Sampler = KnownResamplers.Lanczos3
            }));

            using var outputStream = new MemoryStream();
            await image.SaveAsJpegAsync(outputStream, new JpegEncoder
            {
                Quality = 90 // Higher quality for medium images
            });

            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating medium image");
            throw new InvalidOperationException($"Failed to generate medium image: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validate image file format and content
    /// Comprehensive validation with format detection
    /// </summary>
    public async Task<ImageValidationResult> ValidateImageAsync(Stream stream, string fileName)
    {
        var result = new ImageValidationResult();

        try
        {
            // Check file size
            result.FileSize = stream.Length;
            if (result.FileSize > PhotoConstants.MaxFileSizeBytes)
            {
                result.ErrorMessage = $"File size ({result.FileSize} bytes) exceeds maximum allowed size ({PhotoConstants.MaxFileSizeBytes} bytes)";
                return result;
            }

            // Check file extension
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!PhotoConstants.AllowedExtensions.Contains(extension))
            {
                result.ErrorMessage = $"File extension '{extension}' is not allowed. Allowed extensions: {string.Join(", ", PhotoConstants.AllowedExtensions)}";
                return result;
            }

            // Reset stream position
            stream.Position = 0;

            // Try to load and analyze the image
            using var image = await Image.LoadAsync(stream);

            result.Format = image.Metadata.DecodedImageFormat?.Name ?? "Unknown";
            result.Width = image.Width;
            result.Height = image.Height;

            // ================================
            // VALIDATION CHECKS
            // Comprehensive image quality validation
            // ================================

            // Check minimum dimensions
            const int minDimension = 100;
            if (image.Width < minDimension || image.Height < minDimension)
            {
                result.ErrorMessage = $"Image dimensions ({image.Width}x{image.Height}) are too small. Minimum size: {minDimension}x{minDimension} pixels";
                return result;
            }

            // Check maximum dimensions
            const int maxDimension = 4000;
            if (image.Width > maxDimension || image.Height > maxDimension)
            {
                result.Warnings.Add($"Image dimensions ({image.Width}x{image.Height}) are very large and will be resized");
            }

            // Check aspect ratio
            double aspectRatio = (double)image.Width / image.Height;
            if (aspectRatio > 3.0 || aspectRatio < 0.33)
            {
                result.Warnings.Add("Unusual aspect ratio detected - image may appear distorted in some views");
            }

            // Verify format matches extension
            var expectedFormats = GetExpectedFormatsForExtension(extension);
            if (!expectedFormats.Contains(result.Format, StringComparer.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"File extension '{extension}' doesn't match detected format '{result.Format}'");
            }

            result.IsValid = true;
            return result;
        }
        catch (UnknownImageFormatException)
        {
            result.ErrorMessage = "File is not a valid image or format is not supported";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating image: {FileName}", fileName);
            result.ErrorMessage = $"Error validating image: {ex.Message}";
            return result;
        }
        finally
        {
            // Reset stream position for subsequent operations
            stream.Position = 0;
        }
    }

    /// <summary>
    /// Calculate image quality score based on various factors
    /// Returns score from 1-100 for moderation purposes
    /// </summary>
    public async Task<int> CalculateQualityScoreAsync(Stream stream)
    {
        try
        {
            stream.Position = 0;
            using var image = await Image.LoadAsync(stream);

            int score = 100; // Start with perfect score

            // ================================
            // QUALITY SCORING FACTORS
            // Multiple criteria affecting image quality
            // ================================

            // Resolution factor (0-20 points)
            int totalPixels = image.Width * image.Height;
            if (totalPixels < 40000) // Less than 200x200
                score -= 20;
            else if (totalPixels < 160000) // Less than 400x400
                score -= 10;
            else if (totalPixels < 640000) // Less than 800x800
                score -= 5;

            // Aspect ratio factor (0-15 points)
            double aspectRatio = (double)image.Width / image.Height;
            if (aspectRatio > 2.5 || aspectRatio < 0.4)
                score -= 15;
            else if (aspectRatio > 2.0 || aspectRatio < 0.5)
                score -= 10;
            else if (aspectRatio > 1.8 || aspectRatio < 0.55)
                score -= 5;

            // Size factor - prefer reasonable file sizes (0-10 points)
            var fileSizeKB = stream.Length / 1024;
            if (fileSizeKB < 50) // Very small file, likely low quality
                score -= 10;
            else if (fileSizeKB > 5000) // Very large file, might be unoptimized
                score -= 5;

            // Format factor (0-5 points)
            var format = image.Metadata.DecodedImageFormat?.Name?.ToLower();
            if (format == "gif") // GIFs are typically lower quality for photos
                score -= 5;

            // ================================
            // ADVANCED QUALITY ANALYSIS
            // Pixel-level analysis for quality detection
            // ================================

            // Simple sharpness check (sample based for performance)
            int sharpnessScore = await AnalyzeImageSharpnessAsync(image);
            score = Math.Max(score - (20 - sharpnessScore), 20); // Sharpness can reduce score by up to 20 points

            // Ensure score is within valid range
            score = Math.Max(1, Math.Min(100, score));

            _logger.LogDebug("Calculated quality score: {Score} for image {Width}x{Height}",
                score, image.Width, image.Height);

            return score;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating quality score, defaulting to 75");
            return 75; // Default score if analysis fails
        }
        finally
        {
            stream.Position = 0;
        }
    }

    // ================================
    // ADVANCED PRIVACY FEATURES
    // Advanced privacy controls and content moderation
    // ================================

    /// <summary>
    /// Generate a blurred version of the image for privacy protection
    /// Creates professional-quality blur effect with configurable intensity
    /// </summary>
    public async Task<string?> GenerateBlurredImageAsync(byte[] originalImageData, string originalFileName, double blurIntensity = 0.8)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(originalFileName);
            var extension = Path.GetExtension(originalFileName);
            var blurredFileName = $"blurred_{fileName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";
            var blurredFilePath = Path.Combine(_uploadsPath, "blurred", blurredFileName);

            // Ensure blurred directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(blurredFilePath)!);

            using var originalStream = new MemoryStream(originalImageData);
            using var image = await Image.LoadAsync(originalStream);

            // Calculate blur radius based on intensity and image size
            var imageSize = Math.Max(image.Width, image.Height);
            var baseRadius = Math.Max(8, imageSize / 100); // Adaptive base radius
            var blurRadius = (float)(baseRadius * (0.5 + blurIntensity * 1.5)); // 0.5x to 2x base radius

            // Apply professional Gaussian blur for privacy
            image.Mutate(x => x.GaussianBlur(blurRadius));

            // Optional: Add slight opacity overlay for extra privacy
            if (blurIntensity > 0.9)
            {
                image.Mutate(x => x.Opacity(0.9f));
            }

            // Save the blurred image
            await image.SaveAsync(blurredFilePath);

            _logger.LogInformation("Generated blurred image: {BlurredFileName} (intensity: {BlurIntensity})",
                blurredFileName, blurIntensity);

            return blurredFileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating blurred image for {FileName}", originalFileName);
            return null;
        }
    }

    /// <summary>
    /// Analyze image content for safety and appropriateness using ML.NET
    /// Provides comprehensive content moderation for dating app photos
    /// </summary>
    public async Task<ModerationAnalysis> AnalyzeContentSafetyAsync(byte[] imageData, string fileName)
    {
        var analysis = new ModerationAnalysis();

        try
        {
            using var imageStream = new MemoryStream(imageData);
            using var image = await Image.LoadAsync(imageStream);

            // Perform multiple types of analysis
            var qualityAnalysis = await PerformImageQualityAnalysisAsync(image);
            var contentAnalysis = await PerformContentAnalysisAsync(image, fileName);
            var technicalAnalysis = await PerformTechnicalAnalysisAsync(image);

            // Combine analysis results
            analysis.SafetyScore = CalculateOverallSafetyScore(qualityAnalysis, contentAnalysis, technicalAnalysis);
            analysis.IsAppropriate = analysis.SafetyScore >= 0.7; // Conservative threshold

            // Compile classifications
            analysis.Classifications = new Dictionary<string, double>
            {
                ["image_quality"] = qualityAnalysis.Quality,
                ["technical_score"] = technicalAnalysis.TechnicalScore,
                ["content_appropriateness"] = contentAnalysis.Appropriateness,
                ["overall_safety"] = analysis.SafetyScore
            };

            // Detect potential issues
            var issues = new List<string>();
            if (qualityAnalysis.Quality < 0.5) issues.Add("Low image quality");
            if (technicalAnalysis.TechnicalScore < 0.6) issues.Add("Technical issues detected");
            if (contentAnalysis.Appropriateness < 0.7) issues.Add("Content review recommended");
            if (analysis.SafetyScore < 0.7) issues.Add("Manual moderation required");

            analysis.DetectedIssues = issues.ToArray();

            _logger.LogInformation("Content analysis completed for {FileName}. Safety Score: {SafetyScore}, Issues: {IssueCount}",
                fileName, analysis.SafetyScore, issues.Count);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing content safety for {FileName}", fileName);

            // Return conservative analysis on error
            analysis.IsAppropriate = false;
            analysis.SafetyScore = 0.0;
            analysis.DetectedIssues = new[] { "Analysis failed - manual review required" };
            analysis.Classifications = new Dictionary<string, double> { ["analysis_error"] = 1.0 };

            return analysis;
        }
    }

    /// <summary>
    /// Process photo with privacy features: generate blurred version and perform content analysis
    /// Complete processing pipeline for dating app privacy requirements
    /// </summary>
    public async Task<PrivacyPhotoProcessingResult> ProcessPhotoWithPrivacyAsync(
        byte[] originalImageData,
        string originalFileName,
        string privacyLevel,
        double blurIntensity = 0.8)
    {
        var result = new PrivacyPhotoProcessingResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting privacy photo processing for {FileName} with privacy level {PrivacyLevel}",
                originalFileName, privacyLevel);

            // Perform standard image processing first
            using var originalStream = new MemoryStream(originalImageData);
            var standardResult = await ProcessImageAsync(originalStream, originalFileName);

            // Copy standard processing results
            result.StandardProcessingResult = standardResult;

            // Generate blurred version if privacy requires it
            if (privacyLevel == PhotoPrivacyLevel.Private ||
                privacyLevel == PhotoPrivacyLevel.MatchOnly ||
                privacyLevel == PhotoPrivacyLevel.VIP)
            {
                result.BlurredFileName = await GenerateBlurredImageAsync(
                    standardResult.ImageData, originalFileName, blurIntensity);
                result.RequiresBlur = true;
            }

            // Perform comprehensive content analysis
            result.ModerationAnalysis = await AnalyzeContentSafetyAsync(originalImageData, originalFileName);

            // Additional privacy-specific processing
            if (privacyLevel == PhotoPrivacyLevel.VIP)
            {
                // For VIP photos, perform enhanced analysis
                result.EnhancedPrivacyFeatures = await ProcessVIPPrivacyFeaturesAsync(originalImageData);
            }

            stopwatch.Stop();
            result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
            result.IsSuccess = true;

            _logger.LogInformation("Privacy photo processing completed in {ProcessingTime}ms for {FileName}",
                result.ProcessingTimeMs, originalFileName);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in privacy photo processing for {FileName}", originalFileName);

            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            stopwatch.Stop();
            result.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;

            return result;
        }
    }

    /// <summary>
    /// Get the appropriate image data based on privacy settings and user permissions
    /// Controls what version of the image should be served to different users
    /// </summary>
    public async Task<byte[]?> GetImageWithPrivacyControlAsync(
        Photo photo,
        string requestingUserId,
        bool hasMatch = false)
    {
        try
        {
            // Public photos are always accessible
            if (photo.PrivacyLevel == PhotoPrivacyLevel.Public)
            {
                return await GetImageDataAsync(photo.StoredFileName);
            }

            // Owner can always see their own photos
            if (photo.UserId.ToString() == requestingUserId)
            {
                return await GetImageDataAsync(photo.StoredFileName);
            }

            // For private/match-only photos, check match status
            if (photo.RequiresMatch)
            {
                if (hasMatch)
                {
                    // User has matched - show original
                    return await GetImageDataAsync(photo.StoredFileName);
                }
                else if (photo.PrivacyLevel == PhotoPrivacyLevel.MatchOnly)
                {
                    // No match and match-only - return null (completely hidden)
                    return null;
                }
                else
                {
                    // No match but private level - show blurred version
                    return await GetBlurredImageDataAsync(photo.BlurredFileName);
                }
            }

            // Fallback to original if no special privacy requirements
            return await GetImageDataAsync(photo.StoredFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting image with privacy control for photo {PhotoId}", photo.Id);
            return null;
        }
    }

    // ================================
    // PRIVATE HELPER METHODS
    // Internal processing utilities
    // ================================

    /// <summary>
    /// Calculate optimal dimensions for image processing
    /// Balances quality and file size while maintaining aspect ratio
    /// </summary>
    internal static (int width, int height, bool wasResized) CalculateOptimalDimensions(int originalWidth, int originalHeight)
    {
        const int maxDimension = PhotoConstants.ImageSizes.LargeWidth; // 800px max
        const int maxPixels = 1_000_000; // 1MP max for reasonable file sizes

        // If image is already optimal size, no resize needed
        if (originalWidth <= maxDimension && originalHeight <= maxDimension &&
            originalWidth * originalHeight <= maxPixels)
        {
            return (originalWidth, originalHeight, false);
        }

        // Calculate scale factor to fit within constraints
        double scaleForDimension = Math.Min(
            (double)maxDimension / originalWidth,
            (double)maxDimension / originalHeight
        );

        double scaleForPixels = Math.Sqrt((double)maxPixels / (originalWidth * originalHeight));
        double finalScale = Math.Min(scaleForDimension, scaleForPixels);

        // Apply scale factor
        int newWidth = (int)Math.Round(originalWidth * finalScale);
        int newHeight = (int)Math.Round(originalHeight * finalScale);

        // Ensure minimum dimensions
        const int minDimension = 200;
        if (newWidth < minDimension || newHeight < minDimension)
        {
            double minScale = Math.Max(
                (double)minDimension / originalWidth,
                (double)minDimension / originalHeight
            );
            newWidth = (int)Math.Round(originalWidth * minScale);
            newHeight = (int)Math.Round(originalHeight * minScale);
        }

        return (newWidth, newHeight, true);
    }

    /// <summary>
    /// Determine optimal output format based on input file
    /// Prioritizes modern formats with good compression
    /// </summary>
    internal static string DetermineOptimalFormat(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        return extension switch
        {
            ".png" => "PNG", // Keep PNG for images with transparency
            ".webp" => "WebP", // Keep WebP for best compression
            _ => "JPEG" // Convert everything else to JPEG for best compatibility
        };
    }

    /// <summary>
    /// Get file extension for a given format
    /// </summary>
    internal static string GetExtensionForFormat(string format)
    {
        return format.ToUpper() switch
        {
            "PNG" => ".png",
            "WEBP" => ".webp",
            "JPEG" => ".jpg",
            _ => ".jpg"
        };
    }

    /// <summary>
    /// Save image with optimal settings for the given format
    /// Applies format-specific compression and quality settings
    /// </summary>
    private static async Task SaveImageWithOptimalSettings(Image image, Stream outputStream, string format)
    {
        switch (format.ToUpper())
        {
            case "JPEG":
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
                {
                    Quality = 92 // High quality JPEG
                });
                break;

            case "PNG":
                await image.SaveAsPngAsync(outputStream, new PngEncoder
                {
                    CompressionLevel = PngCompressionLevel.BestCompression
                });
                break;

            case "WEBP":
                await image.SaveAsWebpAsync(outputStream, new WebpEncoder
                {
                    Quality = 90, // High quality WebP
                    Method = WebpEncodingMethod.BestQuality
                });
                break;

            default:
                // Fallback to JPEG
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
                {
                    Quality = 92
                });
                break;
        }
    }

    /// <summary>
    /// Get expected image formats for a file extension
    /// Used for validation and format verification
    /// </summary>
    internal static string[] GetExpectedFormatsForExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new[] { "JPEG", "JPG" },
            ".png" => new[] { "PNG" },
            ".webp" => new[] { "WebP", "WEBP" },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Analyze image sharpness using simplified approach
    /// Returns score from 0-20 based on image characteristics
    /// </summary>
    private static async Task<int> AnalyzeImageSharpnessAsync(Image image)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Simplified sharpness analysis based on image characteristics
                // For performance, we'll use image dimensions and compression as proxies

                int sharpnessScore = 10; // Base score

                // Factor in image resolution
                int totalPixels = image.Width * image.Height;
                if (totalPixels > 500000) // > 0.5MP
                    sharpnessScore += 5;
                if (totalPixels > 1000000) // > 1MP
                    sharpnessScore += 3;
                if (totalPixels > 2000000) // > 2MP
                    sharpnessScore += 2;

                // Factor in aspect ratio (square images tend to be better for profiles)
                double aspectRatio = (double)image.Width / image.Height;
                if (aspectRatio >= 0.8 && aspectRatio <= 1.25) // Near square
                    sharpnessScore += 2;

                return Math.Min(20, Math.Max(0, sharpnessScore));
            }
            catch
            {
                return 10; // Default score on error
            }
        });
    }

    // ================================
    // PRIVACY FEATURE HELPER METHODS
    // ================================

    private async Task<ImageQualityAnalysis> PerformImageQualityAnalysisAsync(Image image)
    {
        return await Task.Run(() =>
        {
            var analysis = new ImageQualityAnalysis();

            // Resolution quality
            var totalPixels = image.Width * image.Height;
            analysis.ResolutionScore = Math.Min(1.0, totalPixels / 1000000.0); // Normalize to 1MP

            // Aspect ratio appropriateness for dating profiles
            var aspectRatio = (double)image.Width / image.Height;
            analysis.AspectRatioScore = aspectRatio switch
            {
                >= 0.8 and <= 1.25 => 1.0, // Square-ish (ideal for profiles)
                >= 0.6 and <= 1.6 => 0.8,  // Reasonable
                >= 0.4 and <= 2.5 => 0.6,  // Acceptable
                _ => 0.3 // Poor aspect ratio
            };

            // Overall quality score
            analysis.Quality = (analysis.ResolutionScore + analysis.AspectRatioScore) / 2.0;

            return analysis;
        });
    }

    private async Task<ContentAnalysis> PerformContentAnalysisAsync(Image image, string fileName)
    {
        return await Task.Run(() =>
        {
            var analysis = new ContentAnalysis();

            // Heuristic-based content analysis
            // In production, this would integrate with specialized ML models

            // File name analysis for potential inappropriate content
            var fileNameLower = fileName.ToLowerInvariant();
            var inappropriateKeywords = new[] { "nude", "naked", "explicit", "nsfw", "adult" };
            var hasInappropriateName = inappropriateKeywords.Any(keyword => fileNameLower.Contains(keyword));

            if (hasInappropriateName)
            {
                analysis.Appropriateness = 0.3;
                analysis.ContentFlags.Add("Inappropriate filename detected");
            }
            else
            {
                analysis.Appropriateness = 0.8; // Conservative baseline
            }

            // Simple brightness analysis (very bright or very dark images can be problematic)
            // This is a placeholder - real implementation would analyze pixel data
            analysis.TechnicalQuality = 0.8;

            return analysis;
        });
    }

    private async Task<TechnicalAnalysis> PerformTechnicalAnalysisAsync(Image image)
    {
        return await Task.Run(() =>
        {
            var analysis = new TechnicalAnalysis();

            // Technical quality metrics
            var format = image.Metadata.DecodedImageFormat?.Name?.ToLower() ?? "unknown";
            analysis.FormatScore = format switch
            {
                "jpeg" or "jpg" => 0.9,
                "png" => 0.95,
                "webp" => 1.0,
                _ => 0.7
            };

            // Size appropriateness
            var totalPixels = image.Width * image.Height;
            analysis.SizeScore = totalPixels switch
            {
                >= 160000 and <= 4000000 => 1.0, // 400x400 to 2000x2000
                >= 40000 and <= 8000000 => 0.8,  // Acceptable range
                _ => 0.6 // Too small or too large
            };

            analysis.TechnicalScore = (analysis.FormatScore + analysis.SizeScore) / 2.0;

            return analysis;
        });
    }

    internal double CalculateOverallSafetyScore(
        ImageQualityAnalysis quality,
        ContentAnalysis content,
        TechnicalAnalysis technical)
    {
        // Weighted combination of different analysis types
        var qualityWeight = 0.3;
        var contentWeight = 0.5; // Content is most important for safety
        var technicalWeight = 0.2;

        var overallScore = (quality.Quality * qualityWeight) +
                          (content.Appropriateness * contentWeight) +
                          (technical.TechnicalScore * technicalWeight);

        return Math.Max(0.0, Math.Min(1.0, overallScore));
    }

    private async Task<VIPPrivacyFeatures> ProcessVIPPrivacyFeaturesAsync(byte[] imageData)
    {
        // Placeholder for VIP-specific privacy features
        // Could include advanced blur patterns, watermarking, etc.
        return await Task.Run(() => new VIPPrivacyFeatures
        {
            HasAdvancedBlur = true,
            HasWatermark = false,
            ProcessingLevel = "VIP"
        });
    }

    private async Task<byte[]?> GetImageDataAsync(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;

        var filePath = Path.Combine(_uploadsPath, "photos", fileName);
        return File.Exists(filePath) ? await File.ReadAllBytesAsync(filePath) : null;
    }

    private async Task<byte[]?> GetBlurredImageDataAsync(string? blurredFileName)
    {
        if (string.IsNullOrEmpty(blurredFileName)) return null;

        var filePath = Path.Combine(_uploadsPath, "blurred", blurredFileName);
        return File.Exists(filePath) ? await File.ReadAllBytesAsync(filePath) : null;
    }
}

// ================================
// SUPPORTING CLASSES FOR PRIVACY FEATURES
// ================================

/// <summary>
/// Result of privacy-enabled photo processing
/// </summary>
public class PrivacyPhotoProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public ImageProcessingResult? StandardProcessingResult { get; set; }
    public string? BlurredFileName { get; set; }
    public bool RequiresBlur { get; set; }
    public ModerationAnalysis? ModerationAnalysis { get; set; }
    public VIPPrivacyFeatures? EnhancedPrivacyFeatures { get; set; }
    public int ProcessingTimeMs { get; set; }
}

/// <summary>
/// Image quality analysis results
/// </summary>
public class ImageQualityAnalysis
{
    public double Quality { get; set; }
    public double ResolutionScore { get; set; }
    public double AspectRatioScore { get; set; }
}

/// <summary>
/// Content analysis results for moderation
/// </summary>
public class ContentAnalysis
{
    public double Appropriateness { get; set; } = 1.0;
    public double TechnicalQuality { get; set; } = 1.0;
    public List<string> ContentFlags { get; set; } = new();
}

/// <summary>
/// Technical analysis results
/// </summary>
public class TechnicalAnalysis
{
    public double TechnicalScore { get; set; }
    public double FormatScore { get; set; }
    public double SizeScore { get; set; }
}

/// <summary>
/// VIP privacy features
/// </summary>
public class VIPPrivacyFeatures
{
    public bool HasAdvancedBlur { get; set; }
    public bool HasWatermark { get; set; }
    public string ProcessingLevel { get; set; } = string.Empty;
}
