using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;

namespace PhotoService.Controllers;

[ApiController]
[Route("api/photos")]
public class PhotoDeletionController : ControllerBase
{
    private readonly PhotoContext _context;
    private readonly ILogger<PhotoDeletionController> _logger;

    public PhotoDeletionController(PhotoContext context, ILogger<PhotoDeletionController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Cascade delete all photos for a user (called during account deletion)
    /// </summary>
    [HttpDelete("user/{userProfileId:int}")]
    [AllowAnonymous] // Service-to-service call from UserService
    public async Task<IActionResult> DeleteUserPhotos(int userProfileId)
    {
        try
        {
            var photos = await _context.Photos
                .Where(p => p.UserId == userProfileId)
                .ToListAsync();

            var count = photos.Count;

            if (count > 0)
            {
                _context.Photos.RemoveRange(photos);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Deleted {Count} photos for user {UserId}", count, userProfileId);
            }

            return Ok(count.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting photos for user {UserId}", userProfileId);
            return StatusCode(500, "0"); // Return 0 count on error, allow cascade to continue
        }
    }
}
