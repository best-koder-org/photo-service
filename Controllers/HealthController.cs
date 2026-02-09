using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoService.Data;
using Microsoft.EntityFrameworkCore;

namespace PhotoService.Controllers;

/// <summary>
/// Health check controller for photo-service.
/// Reports service health, database connectivity, and storage availability.
/// Consistent with HealthController pattern in other services.
/// </summary>
[Route("api/health")]
[ApiController]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly PhotoContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HealthController> _logger;

    public HealthController(PhotoContext context, IWebHostEnvironment env, ILogger<HealthController> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Basic health check — always returns 200 if service is running.
    /// </summary>
    [HttpGet]
    public IActionResult GetHealth()
    {
        return Ok(new HealthResponse
        {
            Status = "Healthy",
            Service = "PhotoService",
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Detailed health check — includes database and storage status.
    /// </summary>
    [HttpGet("detailed")]
    public async Task<IActionResult> GetDetailedHealth()
    {
        var dbHealthy = false;
        var dbMessage = "";
        var photoCount = 0;

        try
        {
            dbHealthy = await _context.Database.CanConnectAsync();
            if (dbHealthy)
            {
                photoCount = await _context.Photos.CountAsync();
            }
            dbMessage = dbHealthy ? "Connected" : "Cannot connect";
        }
        catch (Exception ex)
        {
            dbMessage = $"Error: {ex.Message}";
            _logger.LogWarning(ex, "Health check: database connectivity failed");
        }

        var storageHealthy = false;
        var storagePath = Path.Combine(_env.ContentRootPath, "uploads", "photos");
        try
        {
            storageHealthy = Directory.Exists(storagePath);
            if (!storageHealthy)
            {
                Directory.CreateDirectory(storagePath);
                storageHealthy = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check: storage directory check failed");
        }

        var overallHealthy = dbHealthy && storageHealthy;

        return overallHealthy ? Ok(new DetailedHealthResponse
        {
            Status = "Healthy",
            Service = "PhotoService",
            Timestamp = DateTime.UtcNow,
            Database = new ComponentHealth { Status = dbMessage, Healthy = dbHealthy },
            Storage = new ComponentHealth { Status = storageHealthy ? "Available" : "Unavailable", Healthy = storageHealthy },
            Stats = new ServiceStats { TotalPhotos = photoCount }
        }) : StatusCode(503, new DetailedHealthResponse
        {
            Status = "Degraded",
            Service = "PhotoService",
            Timestamp = DateTime.UtcNow,
            Database = new ComponentHealth { Status = dbMessage, Healthy = dbHealthy },
            Storage = new ComponentHealth { Status = storageHealthy ? "Available" : "Unavailable", Healthy = storageHealthy },
            Stats = new ServiceStats { TotalPhotos = photoCount }
        });
    }
}

public class HealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class DetailedHealthResponse : HealthResponse
{
    public ComponentHealth Database { get; set; } = new();
    public ComponentHealth Storage { get; set; } = new();
    public ServiceStats Stats { get; set; } = new();
}

public class ComponentHealth
{
    public string Status { get; set; } = string.Empty;
    public bool Healthy { get; set; }
}

public class ServiceStats
{
    public int TotalPhotos { get; set; }
}
