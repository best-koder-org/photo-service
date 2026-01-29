using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;

namespace PhotoService.Services;

/// <summary>
/// Background service for cleaning up orphaned photos
/// Runs periodically to remove soft-deleted photos and files without database references
/// </summary>
public class PhotoCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PhotoCleanupBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _defaultRunInterval = TimeSpan.FromHours(24); // Daily by default

    public PhotoCleanupBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<PhotoCleanupBackgroundService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Photo Cleanup Background Service started");

        // Get configuration
        var enabled = _configuration.GetValue<bool>("PhotoCleanup:Enabled", true);
        var runInterval = _configuration.GetValue<TimeSpan?>("PhotoCleanup:RunInterval") ?? _defaultRunInterval;
        var runAtHour = _configuration.GetValue<int?>("PhotoCleanup:RunAtHour"); // UTC hour (0-23)
        var softDeleteGracePeriodDays = _configuration.GetValue<int>("PhotoCleanup:SoftDeleteGracePeriodDays", 30);

        if (!enabled)
        {
            _logger.LogInformation("Photo cleanup is disabled in configuration");
            return;
        }

        _logger.LogInformation(
            "Photo cleanup configured: Interval={Interval}, RunAtHour={Hour}, GracePeriod={Days} days",
            runInterval, runAtHour?.ToString() ?? "any", softDeleteGracePeriodDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Calculate delay until next run
                TimeSpan delay;
                if (runAtHour.HasValue)
                {
                    delay = CalculateDelayUntilTargetHour(runAtHour.Value);
                    _logger.LogInformation("Next cleanup scheduled at {NextRun} UTC", DateTime.UtcNow.Add(delay));
                }
                else
                {
                    delay = runInterval;
                    _logger.LogInformation("Next cleanup scheduled in {Delay}", delay);
                }

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                // Run cleanup
                await RunCleanupAsync(softDeleteGracePeriodDays, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Photo cleanup service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in photo cleanup background service");
                // Wait before retrying after error
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Photo Cleanup Background Service stopped");
    }

    private async Task RunCleanupAsync(int gracePeriodDays, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting photo cleanup job");
        var startTime = DateTime.UtcNow;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PhotoContext>();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        try
        {
            // Clean up soft-deleted photos older than grace period
            var gracePeriodCutoff = DateTime.UtcNow.AddDays(-gracePeriodDays);
            
            _logger.LogInformation(
                "Finding soft-deleted photos older than {GracePeriod} days (before {Cutoff})",
                gracePeriodDays, gracePeriodCutoff);

            var softDeletedPhotos = await context.Photos
                .Where(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt < gracePeriodCutoff)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} soft-deleted photos eligible for cleanup", softDeletedPhotos.Count);

            int deletedFiles = 0;
            int deletedRecords = 0;
            long freedBytes = 0;

            foreach (var photo in softDeletedPhotos)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Delete physical files
                    var filesToDelete = new List<string>
                    {
                        photo.FilePath,
                        GetThumbnailPath(photo.StoredFileName, photo.UserId),
                        GetMediumPath(photo.StoredFileName, photo.UserId)
                    };

                    if (!string.IsNullOrEmpty(photo.BlurredFileName))
                    {
                        filesToDelete.Add($"{photo.UserId}/{photo.BlurredFileName}");
                    }

                    foreach (var filePath in filesToDelete)
                    {
                        if (await storageService.ImageExistsAsync(filePath))
                        {
                            var fileSize = await storageService.GetFileSizeAsync(filePath);
                            if (await storageService.DeleteImageAsync(filePath))
                            {
                                deletedFiles++;
                                freedBytes += fileSize;
                                _logger.LogDebug("Deleted file: {FilePath} ({Size} bytes)", filePath, fileSize);
                            }
                        }
                    }

                    // Delete database record (hard delete)
                    context.Photos.Remove(photo);
                    deletedRecords++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete photo {PhotoId} for user {UserId}", 
                        photo.Id, photo.UserId);
                }
            }

            // Save deletions
            if (deletedRecords > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Deleted {Records} photo records and {Files} files, freed {Bytes} bytes ({MB:F2} MB)",
                    deletedRecords, deletedFiles, freedBytes, freedBytes / 1024.0 / 1024.0);
            }

            // Clean up orphaned files without database references
            _logger.LogInformation("Checking for orphaned files without database references");

            var allActivePhotos = await context.Photos
                .Where(p => !p.IsDeleted)
                .Select(p => new { p.UserId, p.StoredFileName, p.BlurredFileName })
                .ToListAsync(cancellationToken);

            var validFilePaths = new List<string>();
            foreach (var photo in allActivePhotos)
            {
                validFilePaths.Add($"{photo.UserId}/{photo.StoredFileName}");
                validFilePaths.Add(GetThumbnailPath(photo.StoredFileName, photo.UserId));
                validFilePaths.Add(GetMediumPath(photo.StoredFileName, photo.UserId));
                
                if (!string.IsNullOrEmpty(photo.BlurredFileName))
                {
                    validFilePaths.Add($"{photo.UserId}/{photo.BlurredFileName}");
                }
            }

            _logger.LogDebug("Found {Count} valid file paths in database", validFilePaths.Count);

            var orphanedFilesCleanedCount = await storageService.CleanupOrphanedFilesAsync(validFilePaths);

            _logger.LogInformation("Cleaned up {Count} orphaned files", orphanedFilesCleanedCount);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Photo cleanup job completed in {Duration}. Summary: {DeletedRecords} DB records deleted, " +
                "{DeletedFiles} files deleted, {OrphanedFiles} orphaned files cleaned, {FreedSpace:F2} MB freed",
                duration, deletedRecords, deletedFiles, orphanedFilesCleanedCount, freedBytes / 1024.0 / 1024.0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during photo cleanup job");
            throw;
        }
    }

    private static TimeSpan CalculateDelayUntilTargetHour(int targetHour)
    {
        var now = DateTime.UtcNow;
        var today = now.Date.AddHours(targetHour);
        var targetTime = now.Hour < targetHour ? today : today.AddDays(1);
        return targetTime - now;
    }

    private static string GetThumbnailPath(string storedFileName, int userId)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(storedFileName);
        var extension = Path.GetExtension(storedFileName);
        return $"{userId}/{nameWithoutExt}_thumb{extension}";
    }

    private static string GetMediumPath(string storedFileName, int userId)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(storedFileName);
        var extension = Path.GetExtension(storedFileName);
        return $"{userId}/{nameWithoutExt}_medium{extension}";
    }
}
