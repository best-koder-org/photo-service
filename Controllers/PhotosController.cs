using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.DTOs;
using PhotoService.Services;
using PhotoService.Models;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace PhotoService.Controllers;

/// <summary>
/// Photo Controller - RESTful API for photo management operations
/// Handles photo upload, retrieval, update, and deletion with JWT authentication
/// Standard REST conventions with comprehensive error handling
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // Require authentication for all endpoints
public class PhotosController : ControllerBase
{
    private readonly IPhotoService _photoService;
    private readonly ILogger<PhotosController> _logger;
    private readonly ISafetyServiceClient _safetyService;
    private readonly IMatchmakingServiceClient _matchmakingService;
    private readonly PhotoContext _context;

    /// <summary>
    /// Constructor with dependency injection
    /// Standard controller pattern with service layer integration
    /// </summary>
    public PhotosController(
        IPhotoService photoService, 
        ILogger<PhotosController> logger,
        ISafetyServiceClient safetyService,
        IMatchmakingServiceClient matchmakingService,
        PhotoContext context)
    {
        _photoService = photoService;
        _logger = logger;
        _safetyService = safetyService;
        _matchmakingService = matchmakingService;
        _context = context;
    }

    /// <summary>
    /// Upload a new photo for the authenticated user
    /// POST /api/photos
    /// Handles multipart form data with photo file and metadata
    /// </summary>
    /// <param name="uploadDto">Photo upload request with file and metadata</param>
    /// <returns>Upload result with photo details or error information</returns>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PhotoUploadResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status413RequestEntityTooLarge)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadPhoto([FromForm] PhotoUploadDto uploadDto)
    {
        try
        {
            var userId = GetCurrentUserId();
            
            _logger.LogInformation("Photo upload request from user {UserId}", userId);

            // ================================
            // INPUT VALIDATION
            // Comprehensive validation before processing
            // ================================

            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .SelectMany(x => x.Value?.Errors?.Select(e => e.ErrorMessage) ?? [])
                    .ToList();
                
                return BadRequest($"Validation failed: {string.Join(", ", errors)}");
            }

            if (uploadDto.Photo == null || uploadDto.Photo.Length == 0)
            {
                return BadRequest("No photo file provided");
            }

            // Check file size at request level
            if (uploadDto.Photo.Length > Models.PhotoConstants.MaxFileSizeBytes)
            {
                return StatusCode(StatusCodes.Status413RequestEntityTooLarge, 
                    $"File size exceeds maximum limit of {Models.PhotoConstants.MaxFileSizeBytes / (1024 * 1024)} MB");
            }

            // ================================
            // BUSINESS LOGIC
            // Delegate to service layer for processing
            // ================================

            var result = await _photoService.UploadPhotoAsync(userId, uploadDto);

            if (!result.Success)
            {
                _logger.LogWarning("Photo upload failed for user {UserId}: {Error}", userId, result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }

            _logger.LogInformation("Photo uploaded successfully for user {UserId}, photo ID {PhotoId}", 
                userId, result.Photo?.Id);

            // ================================
            // SUCCESS RESPONSE
            // Return 201 Created with photo details
            // ================================

            return CreatedAtAction(
                nameof(GetPhoto), 
                new { id = result.Photo!.Id }, 
                result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during photo upload");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while uploading the photo");
        }
    }

    /// <summary>
    /// Get all photos for the authenticated user
    /// GET /api/photos
    /// Returns user's photo collection with metadata and URLs
    /// </summary>
    /// <returns>User photo summary with all photos</returns>
    [HttpGet]
    [ProducesResponseType(typeof(UserPhotoSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserPhotos()
    {
        try
        {
            var userId = GetCurrentUserId();
            var photos = await _photoService.GetUserPhotosAsync(userId);

            _logger.LogDebug("Retrieved {PhotoCount} photos for user {UserId}", 
                photos.TotalPhotos, userId);

            return Ok(photos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user photos");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while retrieving photos");
        }
    }

    /// <summary>
    /// Get a specific photo by ID
    /// GET /api/photos/{id}
    /// Returns photo metadata with ownership validation
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Photo details or 404 if not found/unauthorized</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PhotoResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPhoto(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var photo = await _photoService.GetPhotoAsync(id, userId);

            if (photo == null)
            {
                _logger.LogWarning("Photo {PhotoId} not found for user {UserId}", id, userId);
                return NotFound($"Photo with ID {id} not found");
            }

            return Ok(photo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving photo {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while retrieving the photo");
        }
    }

    /// <summary>
    /// Get user's primary profile photo
    /// GET /api/photos/primary
    /// Returns the primary photo or first photo if none marked as primary
    /// </summary>
    /// <returns>Primary photo or 404 if user has no photos</returns>
    [HttpGet("primary")]
    [ProducesResponseType(typeof(PhotoResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPrimaryPhoto()
    {
        try
        {
            var userId = GetCurrentUserId();
            var photo = await _photoService.GetPrimaryPhotoAsync(userId);

            if (photo == null)
            {
                return NotFound("User has no photos");
            }

            return Ok(photo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving primary photo for user");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while retrieving the primary photo");
        }
    }

    /// <summary>
    /// Serve photo image file
    /// GET /api/photos/{id}/image?size={size}
    /// Returns image file with appropriate content type and caching headers
    /// Enforces privacy: blocked users cannot access each other's photos
    /// MMP Privacy: Non-matched users receive blurred version
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="size">Image size (full, medium, thumbnail)</param>
    /// <returns>Image file stream</returns>
    [HttpGet("{id:int}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPhotoImage(int id, [FromQuery] string size = "full")
    {
        try
        {
            // Get photo owner from database
            var photo = await _context.Photos
                .Where(p => p.Id == id && !p.IsDeleted)
                .Select(p => new { p.UserId })
                .FirstOrDefaultAsync();

            if (photo == null)
            {
                return NotFound("Photo not found");
            }

            // ================================
            // PRIVACY ENFORCEMENT (T052 - MMP Requirement)
            // 1. Block check (existing)
            // 2. Match verification (NEW)
            // 3. Serve blurred version if not matched
            // ================================
            
            bool serveBlurred = false;
            
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    var requestingUserId = GetCurrentUserId();
                    var photoOwnerId = photo.UserId;

                    // Allow users to view their own photos
                    if (photoOwnerId != requestingUserId)
                    {
                        var requesterId = requestingUserId.ToString();
                        var ownerId = photoOwnerId.ToString();

                        // ================================
                        // SAFETY CHECK: Block status
                        // ================================
                        var isBlocked = await _safetyService.IsBlockedAsync(requesterId, ownerId) ||
                                       await _safetyService.IsBlockedAsync(ownerId, requesterId);

                        if (isBlocked)
                        {
                            _logger.LogWarning("User {RequesterId} attempted to access photo {PhotoId} owned by blocked user {PhotoOwnerId}", 
                                requesterId, id, photoOwnerId);
                            return StatusCode(StatusCodes.Status403Forbidden, "Access denied");
                        }

                        // ================================
                        // PRIVACY CHECK: Match verification (T052)
                        // If users are not matched, serve blurred version
                        // ================================
                        var areMatched = await _matchmakingService.AreUsersMatchedAsync(requesterId, ownerId);
                        
                        if (!areMatched)
                        {
                            _logger.LogInformation("User {RequesterId} requested photo {PhotoId} from non-matched user {PhotoOwnerId} - serving blurred version", 
                                requesterId, id, photoOwnerId);
                            serveBlurred = true;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // If user ID cannot be determined, serve blurred (fail secure)
                    _logger.LogDebug("Unable to determine user ID for privacy check, serving blurred version of photo {PhotoId}", id);
                    serveBlurred = true;
                }
            }
            else
            {
                // Unauthenticated users get blurred version (fail secure)
                _logger.LogDebug("Unauthenticated access to photo {PhotoId}, serving blurred version", id);
                serveBlurred = true;
            }

            // ================================
            // SERVE IMAGE (original or blurred)
            // ================================
            Stream stream;
            string contentType;
            string fileName;
            
            if (serveBlurred)
            {
                // Serve blurred version
                var blurResult = await _photoService.GetBlurredPhotoAsync(id);
                
                if (blurResult.ImageData == null)
                {
                    // If blurred version doesn't exist, generate it on-the-fly
                    _logger.LogWarning("Blurred version not found for photo {PhotoId}, generating...", id);
                    var (origStream, _, _) = await _photoService.GetPhotoStreamAsync(id, size);
                    if (origStream == null)
                    {
                        return NotFound("Photo not found");
                    }
                    // For MVP, return 404 if blur doesn't exist (photos should have blur generated on upload)
                    origStream.Dispose();
                    return NotFound("Blurred version not available");
                }
                
                stream = new MemoryStream(blurResult.ImageData);
                contentType = blurResult.ContentType;
                fileName = blurResult.FileName;
            }
            else
            {
                // Serve original version
                (stream, contentType, fileName) = await _photoService.GetPhotoStreamAsync(id, size);
                
                if (stream == null)
                {
                    return NotFound("Photo not found");
                }
            }

            // ================================
            // CACHING HEADERS
            // Optimize image delivery with browser caching
            // ================================
            Response.Headers.CacheControl = "public, max-age=3600"; // Cache for 1 hour
            Response.Headers.ETag = $"\"{id}_{size}_{(serveBlurred ? "blurred" : "original")}\"";
            
            // Check if client has cached version
            var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
            if (etag == $"\"{id}_{size}_{(serveBlurred ? "blurred" : "original")}\"")
            {
                stream.Dispose();
                return StatusCode(StatusCodes.Status304NotModified);
            }

            _logger.LogDebug("Serving {Version} photo {PhotoId} ({Size}) to client", 
                serveBlurred ? "blurred" : "original", id, size);

            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving photo image {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while retrieving the image");
        }
    }

    /// <summary>
    /// Serve photo thumbnail
    /// GET /api/photos/{id}/thumbnail
    /// Convenient endpoint for thumbnail access
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Thumbnail image file</returns>
    [HttpGet("{id:int}/thumbnail")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoThumbnail(int id)
    {
        return await GetPhotoImage(id, "thumbnail");
    }

    /// <summary>
    /// Serve photo medium size
    /// GET /api/photos/{id}/medium
    /// Convenient endpoint for medium-size access
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Medium-size image file</returns>
    [HttpGet("{id:int}/medium")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoMedium(int id)
    {
        return await GetPhotoImage(id, "medium");
    }

    /// <summary>
    /// Update photo metadata
    /// PUT /api/photos/{id}
    /// Updates display order and primary status without re-uploading
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="updateDto">Update request data</param>
    /// <returns>Updated photo details</returns>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(PhotoResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdatePhoto(int id, [FromBody] PhotoUpdateDto updateDto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .SelectMany(x => x.Value?.Errors?.Select(e => e.ErrorMessage) ?? [])
                    .ToList();
                
                return BadRequest($"Validation failed: {string.Join(", ", errors)}");
            }

            var userId = GetCurrentUserId();
            var photo = await _photoService.UpdatePhotoAsync(id, userId, updateDto);

            if (photo == null)
            {
                return NotFound($"Photo with ID {id} not found");
            }

            _logger.LogInformation("Photo {PhotoId} updated by user {UserId}", id, userId);

            return Ok(photo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating photo {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while updating the photo");
        }
    }

    /// <summary>
    /// Reorder multiple photos
    /// PUT /api/photos/reorder
    /// Updates display order for multiple photos in single operation
    /// </summary>
    /// <param name="reorderDto">Reorder request with photo positions</param>
    /// <returns>Updated photo collection</returns>
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(UserPhotoSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReorderPhotos([FromBody] PhotoReorderDto reorderDto)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .SelectMany(x => x.Value?.Errors?.Select(e => e.ErrorMessage) ?? [])
                    .ToList();
                
                return BadRequest($"Validation failed: {string.Join(", ", errors)}");
            }

            var userId = GetCurrentUserId();
            var photos = await _photoService.ReorderPhotosAsync(userId, reorderDto);

            _logger.LogInformation("Photos reordered for user {UserId}: {PhotoCount} photos", 
                userId, reorderDto.Photos.Count);

            return Ok(photos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering photos for user");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while reordering photos");
        }
    }

    /// <summary>
    /// Set photo as primary
    /// PUT /api/photos/{id}/primary
    /// Marks specified photo as user's primary profile photo
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Success status</returns>
    [HttpPut("{id:int}/primary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SetPrimaryPhoto(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var success = await _photoService.SetPrimaryPhotoAsync(id, userId);

            if (!success)
            {
                return NotFound($"Photo with ID {id} not found");
            }

            _logger.LogInformation("Photo {PhotoId} set as primary for user {UserId}", id, userId);

            return Ok(new { message = "Photo set as primary successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting primary photo {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while setting the primary photo");
        }
    }

    /// <summary>
    /// Delete a photo
    /// DELETE /api/photos/{id}
    /// Soft deletes photo and handles primary photo succession
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Success status</returns>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeletePhoto(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var success = await _photoService.DeletePhotoAsync(id, userId);

            if (!success)
            {
                return NotFound($"Photo with ID {id} not found");
            }

            _logger.LogInformation("Photo {PhotoId} deleted by user {UserId}", id, userId);

            return Ok(new { message = "Photo deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting photo {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while deleting the photo");
        }
    }

    /// <summary>
    /// Check if user can upload more photos
    /// GET /api/photos/can-upload
    /// Returns availability for additional photo uploads
    /// </summary>
    /// <returns>Upload availability status</returns>
    [HttpGet("can-upload")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CanUploadMorePhotos()
    {
        try
        {
            var userId = GetCurrentUserId();
            var canUpload = await _photoService.CanUserUploadMorePhotosAsync(userId);

            return Ok(new { 
                canUpload, 
                maxPhotos = Models.PhotoConstants.MaxPhotosPerUser,
                message = canUpload ? "User can upload more photos" : "User has reached photo limit"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking upload availability");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while checking upload availability");
        }
    }

    // ================================
    // ADMIN ENDPOINTS
    // Content moderation and management
    // ================================

    /// <summary>
    /// Get photos for moderation review
    /// GET /api/photos/moderation?status={status}&amp;page={page}&amp;size={size}
    /// Admin endpoint for content moderation workflow
    /// </summary>
    /// <param name="status">Moderation status filter</param>
    /// <param name="page">Page number for pagination</param>
    /// <param name="size">Page size</param>
    /// <returns>Paginated list of photos for review</returns>
    [HttpGet("moderation")]
    [Authorize(Roles = "Admin,Moderator")] // Require admin or moderator role
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPhotosForModeration(
        [FromQuery] string status = Models.ModerationStatus.PendingReview,
        [FromQuery] int page = 1,
        [FromQuery] int size = 50)
    {
        try
        {
            var (photos, totalCount) = await _photoService.GetPhotosForModerationAsync(status, page, size);

            var result = new
            {
                photos,
                pagination = new
                {
                    currentPage = page,
                    pageSize = size,
                    totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / size)
                }
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving photos for moderation");
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while retrieving photos for moderation");
        }
    }

    /// <summary>
    /// Update photo moderation status
    /// PUT /api/photos/{id}/moderation
    /// Admin endpoint for approving/rejecting photos
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="request">Moderation decision</param>
    /// <returns>Success status</returns>
    [HttpPut("{id:int}/moderation")]
    [Authorize(Roles = "Admin,Moderator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateModerationStatus(int id, [FromBody] ModerationUpdateRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest("Invalid moderation request");
            }

            var success = await _photoService.UpdateModerationStatusAsync(id, request.Status, request.Notes);

            if (!success)
            {
                return NotFound($"Photo with ID {id} not found");
            }

            _logger.LogInformation("Photo {PhotoId} moderation status updated to {Status} by moderator", 
                id, request.Status);

            return Ok(new { message = "Moderation status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating moderation status for photo {PhotoId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                "An error occurred while updating moderation status");
        }
    }

    // ================================
    // PRIVATE HELPER METHODS
    // Internal utility functions
    // ================================

    /// <summary>
    /// Extract user ID from JWT claims
    /// Standard JWT authentication pattern
    /// </summary>
    /// <returns>Current user's ID</returns>
    /// <exception cref="UnauthorizedAccessException">If user ID cannot be determined</exception>
    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                         User.FindFirst("sub")?.Value ??
                         User.FindFirst("userId")?.Value;

        if (string.IsNullOrEmpty(userIdClaim))
        {
            _logger.LogError("Unable to determine user ID from claims");
            throw new UnauthorizedAccessException("Unable to determine user identity");
        }

        // Try to parse as int first (for legacy compatibility)
        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        // For string-based user IDs (like IdentityUser), use hash code for integer mapping
        // This provides consistent mapping between string IDs and integer foreign keys
        var hashCode = userIdClaim.GetHashCode();
        // Ensure positive number
        var mappedUserId = Math.Abs(hashCode);
        
        _logger.LogInformation($"Mapped string user ID '{userIdClaim}' to integer {mappedUserId}");
        return mappedUserId;
    }

    // ================================
    // ADVANCED PRIVACY FEATURES
    // ================================

    /// <summary>
    /// Upload a photo with advanced privacy settings
    /// POST /api/photos/privacy
    /// Enhanced upload with privacy level, blur settings, and content moderation
    /// </summary>
    /// <param name="uploadDto">Privacy-enabled upload request</param>
    /// <returns>Upload result with privacy processing details</returns>
    [HttpPost("privacy")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PrivacyPhotoUploadResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadPhotoWithPrivacy([FromForm] PrivacyPhotoUploadDto uploadDto)
    {
        try
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Privacy photo upload requested by user {UserId} with privacy level {PrivacyLevel}", 
                userId, uploadDto.PrivacyLevel);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validate privacy level
            var validPrivacyLevels = new[] { 
                "PUBLIC", 
                "PRIVATE", 
                "MATCH_ONLY", 
                "VIP" 
            };
            
            if (!validPrivacyLevels.Contains(uploadDto.PrivacyLevel))
            {
                return BadRequest($"Invalid privacy level. Valid values: {string.Join(", ", validPrivacyLevels)}");
            }

            var result = await _photoService.UploadPhotoWithPrivacyAsync(userId, uploadDto);

            if (result.Success)
            {
                _logger.LogInformation("Privacy photo uploaded successfully: {PhotoId} for user {UserId}", 
                    result.PhotoId, userId);
                return CreatedAtAction(nameof(GetPhoto), new { id = result.PhotoId }, result);
            }
            else
            {
                _logger.LogWarning("Privacy photo upload failed for user {UserId}: {Error}", userId, result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading privacy photo for user {UserId}", GetCurrentUserId());
            return StatusCode(500, "An error occurred while uploading the photo");
        }
    }

    /// <summary>
    /// Update photo privacy settings
    /// PUT /api/photos/{id}/privacy
    /// Change privacy level, blur intensity, and match requirements
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="privacyDto">Privacy settings update</param>
    /// <returns>Updated photo with new privacy settings</returns>
    [HttpPut("{id:int}/privacy")]
    [ProducesResponseType(typeof(PhotoResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdatePhotoPrivacy(int id, [FromBody] PhotoPrivacyUpdateDto privacyDto)
    {
        try
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Privacy update requested for photo {PhotoId} by user {UserId}", id, userId);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _photoService.UpdatePhotoPrivacyAsync(id, userId, privacyDto);

            if (result == null)
            {
                _logger.LogWarning("Photo {PhotoId} not found or access denied for user {UserId}", id, userId);
                return NotFound($"Photo {id} not found or access denied");
            }

            _logger.LogInformation("Privacy settings updated for photo {PhotoId}", id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating privacy for photo {PhotoId} by user {UserId}", id, GetCurrentUserId());
            return StatusCode(500, "An error occurred while updating photo privacy");
        }
    }

    /// <summary>
    /// Get photo with privacy controls applied
    /// GET /api/photos/{id}/image/privacy
    /// Returns appropriate version (original or blurred) based on privacy settings and user permissions
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="requestingUserId">ID of user requesting access (optional for cross-user access)</param>
    /// <returns>Image data respecting privacy settings</returns>
    [HttpGet("{id:int}/image/privacy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPhotoWithPrivacyControl(int id, [FromQuery] string? requestingUserId = null)
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var targetUserId = requestingUserId ?? currentUserId.ToString();
            
            _logger.LogInformation("Privacy-controlled image request for photo {PhotoId} by user {UserId}", id, currentUserId);

            // TODO: In a real implementation, you'd check match status from a matches service
            // For now, we'll assume no match unless it's the owner
            var hasMatch = targetUserId == currentUserId.ToString();

            var result = await _photoService.GetPhotoWithPrivacyControlAsync(id, targetUserId, hasMatch);

            if (result.ImageData == null)
            {
                if (result.AccessDenied)
                {
                    _logger.LogWarning("Access denied to photo {PhotoId} for user {UserId}", id, currentUserId);
                    return Forbid("Access denied - photo is private and requires a match");
                }
                else
                {
                    _logger.LogWarning("Photo {PhotoId} not found", id);
                    return NotFound($"Photo {id} not found");
                }
            }

            _logger.LogInformation("Serving {ImageType} version of photo {PhotoId} to user {UserId}", 
                result.IsBlurred ? "blurred" : "original", id, currentUserId);

            return File(result.ImageData, result.ContentType, result.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving privacy-controlled image for photo {PhotoId}", id);
            return StatusCode(500, "An error occurred while retrieving the image");
        }
    }

    /// <summary>
    /// Get blurred version of a photo
    /// GET /api/photos/{id}/blurred
    /// Always returns the blurred version regardless of privacy settings
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <returns>Blurred image data</returns>
    [HttpGet("{id:int}/blurred")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBlurredPhoto(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Blurred image request for photo {PhotoId} by user {UserId}", id, userId);

            var result = await _photoService.GetBlurredPhotoAsync(id);

            if (result.ImageData == null)
            {
                _logger.LogWarning("Blurred photo {PhotoId} not found", id);
                return NotFound($"Blurred version of photo {id} not found");
            }

            return File(result.ImageData, result.ContentType, result.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving blurred image for photo {PhotoId}", id);
            return StatusCode(500, "An error occurred while retrieving the blurred image");
        }
    }

    /// <summary>
    /// Regenerate blurred version of a photo
    /// POST /api/photos/{id}/regenerate-blur
    /// Useful when changing blur intensity or fixing processing issues
    /// </summary>
    /// <param name="id">Photo identifier</param>
    /// <param name="blurIntensity">New blur intensity (0.0 to 1.0)</param>
    /// <returns>Processing result</returns>
    [HttpPost("{id:int}/regenerate-blur")]
    [ProducesResponseType(typeof(BlurRegenerationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RegenerateBlurredPhoto(int id, [FromQuery] double blurIntensity = 0.8)
    {
        try
        {
            var userId = GetCurrentUserId();
            _logger.LogInformation("Blur regeneration requested for photo {PhotoId} by user {UserId} with intensity {BlurIntensity}", 
                id, userId, blurIntensity);

            if (blurIntensity < 0.0 || blurIntensity > 1.0)
            {
                return BadRequest("Blur intensity must be between 0.0 and 1.0");
            }

            var result = await _photoService.RegenerateBlurredPhotoAsync(id, userId, blurIntensity);

            if (result == null)
            {
                _logger.LogWarning("Photo {PhotoId} not found or access denied for user {UserId}", id, userId);
                return NotFound($"Photo {id} not found or access denied");
            }

            if (!result.Success)
            {
                _logger.LogWarning("Blur regeneration failed for photo {PhotoId}: {Error}", id, result.ErrorMessage);
                return BadRequest(result.ErrorMessage);
            }

            _logger.LogInformation("Blur regenerated successfully for photo {PhotoId}", id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error regenerating blur for photo {PhotoId} by user {UserId}", id, GetCurrentUserId());
            return StatusCode(500, "An error occurred while regenerating the blurred image");
        }
    }

    /// <summary>
    /// Delete all photos for a specific user
    /// DELETE /api/photos/user/{userProfileId}
    /// Used during account deletion to remove all user photos
    /// </summary>
    /// <param name="userProfileId">User profile ID whose photos should be deleted</param>
    /// <returns>Number of photos deleted</returns>
    [HttpDelete("user/{userProfileId:int}")]
    [AllowAnonymous] // Called by UserService during account deletion
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteUserPhotos(int userProfileId)
    {
        try
        {
            _logger.LogInformation("Deleting all photos for user {UserProfileId}", userProfileId);

            // Note: Photo.UserId field actually stores the UserProfile.Id, not Keycloak UserId
            var photos = await _context.Photos
                .Where(p => p.UserId == userProfileId)
                .ToListAsync();

            var count = photos.Count;

            // Delete photo files from disk
            foreach (var photo in photos)
            {
                try
                {
                    var filePath = Path.Combine("uploads", "photos", userProfileId.ToString(), photo.StoredFileName);
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }

                    // Delete blurred version if it exists
                    if (!string.IsNullOrEmpty(photo.BlurredFileName))
                    {
                        var blurredPath = Path.Combine("uploads", "photos", userProfileId.ToString(), photo.BlurredFileName);
                        if (System.IO.File.Exists(blurredPath))
                        {
                            System.IO.File.Delete(blurredPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error deleting file for photo {PhotoId}", photo.Id);
                }
            }

            // Delete database records
            _context.Photos.RemoveRange(photos);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted {Count} photos for user {UserProfileId}", count, userProfileId);
            return Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting photos for user {UserProfileId}", userProfileId);
            return StatusCode(500, "An error occurred while deleting user photos");
        }
    }
}

/// <summary>
/// Request model for moderation status updates
/// Used by admin endpoints for content moderation
/// </summary>
public class ModerationUpdateRequest
{
    /// <summary>
    /// New moderation status
    /// </summary>
    [Required]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Optional moderation notes
    /// </summary>
    public string? Notes { get; set; }
}
