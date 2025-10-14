namespace PhotoService.Services;

/// <summary>
/// Local File Storage Service implementation
/// Handles file storage operations on the local filesystem
/// Organized by user directories for efficient management
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly ILogger<LocalStorageService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _baseStoragePath;

    /// <summary>
    /// Constructor with dependency injection
    /// Initializes storage configuration and base path
    /// </summary>
    public LocalStorageService(ILogger<LocalStorageService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // Get storage path from configuration or use default
        _baseStoragePath = _configuration.GetValue<string>("Storage:PhotosPath") ?? "wwwroot/uploads/photos";
        
        // Ensure base directory exists
        Directory.CreateDirectory(_baseStoragePath);
        
        _logger.LogInformation("LocalStorageService initialized with base path: {BasePath}", _baseStoragePath);
    }

    /// <summary>
    /// Store image file with unique naming and directory organization
    /// Creates user-specific directories and handles naming conflicts
    /// </summary>
    public async Task<StorageResult> StoreImageAsync(
        Stream stream,
        int userId,
        string originalFileName,
        string suffix = "",
        bool useProvidedFileName = false)
    {
        var result = new StorageResult();

        try
        {
            // ================================
            // DIRECTORY PREPARATION
            // Create user-specific directory structure
            // ================================

            var userDirectory = Path.Combine(_baseStoragePath, userId.ToString());
            Directory.CreateDirectory(userDirectory);

            // ================================
            // FILENAME GENERATION
            // Create unique filename to prevent conflicts
            // ================================

            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var baseFileName = Path.GetFileNameWithoutExtension(originalFileName);

            // Sanitize filename to remove invalid characters while preserving underscores
            baseFileName = SanitizeFileName(baseFileName);

            string fileName;
            string filePath;

            if (useProvidedFileName)
            {
                fileName = string.IsNullOrWhiteSpace(baseFileName)
                    ? $"image{extension}"
                    : $"{baseFileName}{extension}";
                filePath = Path.Combine(userDirectory, fileName);
            }
            else
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var uniqueId = Guid.NewGuid().ToString("N")[..8];
                fileName = $"{userId}_{timestamp}_{uniqueId}{suffix}{extension}";
                filePath = Path.Combine(userDirectory, fileName);
            }

            // ================================
            // CONFLICT RESOLUTION
            // Ensure filename uniqueness
            // ================================

            int counter = 1;
            var baseNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            while (File.Exists(filePath))
            {
                var candidateBase = $"{baseNameWithoutExt}_{counter}";
                fileName = $"{candidateBase}{extension}";
                filePath = Path.Combine(userDirectory, fileName);
                counter++;

                // Safety check to prevent infinite loop
                if (counter > 1000)
                {
                    result.ErrorMessage = "Unable to generate unique filename after 1000 attempts";
                    return result;
                }
            }

            // ================================
            // FILE STORAGE
            // Write stream to file with error handling
            // ================================

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Position = 0; // Reset stream position
                await stream.CopyToAsync(fileStream);
                await fileStream.FlushAsync();
            }

            // ================================
            // VERIFICATION
            // Verify file was written correctly
            // ================================

            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "File was not created successfully";
                return result;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                File.Delete(filePath); // Clean up empty file
                result.ErrorMessage = "Created file is empty";
                return result;
            }

            // ================================
            // SUCCESS RESULT
            // Return storage information
            // ================================

            result.Success = true;
            result.FilePath = GetRelativeFilePath(filePath);
            result.FileName = fileName;
            result.FileSize = fileInfo.Length;

            _logger.LogInformation("Successfully stored image for user {UserId}: {FileName} ({FileSize} bytes)", 
                userId, fileName, result.FileSize);

            return result;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied storing image for user {UserId}", userId);
            result.ErrorMessage = "Access denied - insufficient permissions to store file";
            return result;
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "Directory not found storing image for user {UserId}", userId);
            result.ErrorMessage = "Storage directory not found";
            return result;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error storing image for user {UserId}: {Message}", userId, ex.Message);
            result.ErrorMessage = $"Storage error: {ex.Message}";
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error storing image for user {UserId}", userId);
            result.ErrorMessage = "An unexpected error occurred while storing the file";
            return result;
        }
    }

    /// <summary>
    /// Retrieve image file stream for serving to clients
    /// Returns stream with appropriate buffering for web delivery
    /// </summary>
    public async Task<Stream?> GetImageStreamAsync(string filePath)
    {
        try
        {
            var fullPath = GetFullFilePath(filePath);
            
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Image file not found: {FilePath}", filePath);
                return null;
            }

            // Return FileStream with read-only access and sequential access hint
            // This is optimized for web serving scenarios
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 
                bufferSize: 4096, FileOptions.SequentialScan);

            _logger.LogDebug("Serving image stream: {FilePath}", filePath);
            return await Task.FromResult(stream);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied reading image: {FilePath}", filePath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error reading image: {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error reading image: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Delete image file from storage
    /// Handles soft deletion and cleanup operations
    /// </summary>
    public async Task<bool> DeleteImageAsync(string filePath)
    {
        try
        {
            var fullPath = GetFullFilePath(filePath);
            
            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Attempted to delete non-existent file: {FilePath}", filePath);
                return true; // Consider non-existent file as successfully deleted
            }

            File.Delete(fullPath);
            
            _logger.LogInformation("Successfully deleted image: {FilePath}", filePath);
            return await Task.FromResult(true);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied deleting image: {FilePath}", filePath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error deleting image: {FilePath}", filePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting image: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Check if image file exists in storage
    /// Used for validation and integrity checks
    /// </summary>
    public async Task<bool> ImageExistsAsync(string filePath)
    {
        try
        {
            var fullPath = GetFullFilePath(filePath);
            return await Task.FromResult(File.Exists(fullPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if image exists: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Get file size in bytes
    /// Used for storage quota calculations and metadata
    /// </summary>
    public async Task<long> GetFileSizeAsync(string filePath)
    {
        try
        {
            var fullPath = GetFullFilePath(filePath);
            
            if (!File.Exists(fullPath))
                return 0;

            var fileInfo = new FileInfo(fullPath);
            return await Task.FromResult(fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file size: {FilePath}", filePath);
            return 0;
        }
    }

    /// <summary>
    /// Clean up orphaned files that no longer have database references
    /// Maintenance operation for storage optimization
    /// </summary>
    public async Task<int> CleanupOrphanedFilesAsync(List<string> validFilePaths)
    {
        int cleanedCount = 0;

        try
        {
            _logger.LogInformation("Starting cleanup of orphaned files. Valid files: {ValidCount}", validFilePaths.Count);

            var validFileSet = new HashSet<string>(validFilePaths.Select(GetFullFilePath), StringComparer.OrdinalIgnoreCase);

            // Get all user directories
            var userDirectories = Directory.GetDirectories(_baseStoragePath);

            foreach (var userDir in userDirectories)
            {
                if (!int.TryParse(Path.GetFileName(userDir), out _))
                {
                    // Skip non-user directories
                    continue;
                }

                var files = Directory.GetFiles(userDir, "*", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    if (!validFileSet.Contains(file))
                    {
                        try
                        {
                            // Check file age - only delete files older than 1 hour
                            // This prevents deletion of recently uploaded files that might not be in DB yet
                            var fileInfo = new FileInfo(file);
                            if (DateTime.UtcNow - fileInfo.CreationTimeUtc > TimeSpan.FromHours(1))
                            {
                                File.Delete(file);
                                cleanedCount++;
                                _logger.LogDebug("Deleted orphaned file: {FilePath}", file);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete orphaned file: {FilePath}", file);
                        }
                    }
                }

                // Remove empty user directories
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(userDir).Any())
                    {
                        Directory.Delete(userDir);
                        _logger.LogDebug("Deleted empty user directory: {Directory}", userDir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete empty directory: {Directory}", userDir);
                }
            }

            _logger.LogInformation("Cleanup completed. Deleted {CleanedCount} orphaned files", cleanedCount);
            return await Task.FromResult(cleanedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup operation");
            return cleanedCount;
        }
    }

    // ================================
    // PRIVATE HELPER METHODS
    // Internal utility functions
    // ================================

    /// <summary>
    /// Convert relative file path to absolute file path
    /// Handles path normalization and security
    /// </summary>
    private string GetFullFilePath(string relativePath)
    {
        // Normalize path separators
        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                  .Replace('\\', Path.DirectorySeparatorChar);

        // Remove leading separators to ensure relative path
        relativePath = relativePath.TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(_baseStoragePath, relativePath);

        // Security check: ensure the resolved path is within the base storage directory
        var normalizedBasePath = Path.GetFullPath(_baseStoragePath);
        var normalizedFullPath = Path.GetFullPath(fullPath);

        if (!normalizedFullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Access to path outside storage directory is not allowed: {relativePath}");
        }

        return normalizedFullPath;
    }

    /// <summary>
    /// Convert absolute file path to relative file path
    /// For database storage and API responses
    /// </summary>
    private string GetRelativeFilePath(string fullPath)
    {
        var normalizedBasePath = Path.GetFullPath(_baseStoragePath);
        var normalizedFullPath = Path.GetFullPath(fullPath);

        if (!normalizedFullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path is not within storage directory: {fullPath}");
        }

        var relativePath = Path.GetRelativePath(normalizedBasePath, normalizedFullPath);
        
        // Normalize to forward slashes for web compatibility
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Sanitize filename to remove invalid characters
    /// Ensures safe filesystem operations
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "image";

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());

        // Replace spaces and other problematic characters
        sanitized = sanitized.Replace(' ', '_')
                            .Replace('-', '_')
                            .Replace('(', '_')
                            .Replace(')', '_');

        // Remove consecutive underscores
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }

        // Trim underscores from start and end
        sanitized = sanitized.Trim('_');

        // Ensure we have a valid filename
        if (string.IsNullOrWhiteSpace(sanitized))
            return "image";

        // Limit length to prevent filesystem issues
        const int maxLength = 50;
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength].TrimEnd('_');

        return sanitized;
    }

    /// <summary>
    /// Get storage statistics for monitoring and maintenance
    /// Returns information about storage usage
    /// </summary>
    public async Task<StorageStatistics> GetStorageStatisticsAsync()
    {
        var stats = new StorageStatistics();

        try
        {
            if (!Directory.Exists(_baseStoragePath))
            {
                return stats;
            }

            var allFiles = Directory.GetFiles(_baseStoragePath, "*", SearchOption.AllDirectories);
            
            stats.TotalFiles = allFiles.Length;
            stats.TotalSizeBytes = allFiles.Sum(f => new FileInfo(f).Length);
            
            var userDirectories = Directory.GetDirectories(_baseStoragePath);
            stats.UserDirectories = userDirectories.Length;

            // Calculate by file type
            var extensions = allFiles.GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                                   .ToDictionary(g => g.Key, g => g.Count());
            
            stats.FilesByExtension = extensions;

            _logger.LogDebug("Storage statistics: {TotalFiles} files, {TotalSize} bytes, {UserDirs} user directories",
                stats.TotalFiles, stats.TotalSizeBytes, stats.UserDirectories);

            return await Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating storage statistics");
            return stats;
        }
    }
}

/// <summary>
/// Storage statistics for monitoring and maintenance
/// Provides insights into storage usage and distribution
/// </summary>
public class StorageStatistics
{
    /// <summary>
    /// Total number of files in storage
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Total storage size in bytes
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Number of user directories
    /// </summary>
    public int UserDirectories { get; set; }

    /// <summary>
    /// File count by extension
    /// </summary>
    public Dictionary<string, int> FilesByExtension { get; set; } = new();

    /// <summary>
    /// Human-readable total size
    /// </summary>
    public string TotalSizeFormatted
    {
        get
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double size = TotalSizeBytes;
            int suffixIndex = 0;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F2} {suffixes[suffixIndex]}";
        }
    }

    /// <summary>
    /// Average file size in bytes
    /// </summary>
    public long AverageFileSizeBytes => TotalFiles > 0 ? TotalSizeBytes / TotalFiles : 0;
}
