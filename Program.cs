using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Extensions;
using PhotoService.Services;
using SixLabors.ImageSharp.Web.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// ================================
// PHOTO SERVICE CONFIGURATION
// Standard .NET 8 Web API setup for photo management
// ================================

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger Configuration - Standard API Documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Photo Service API",
        Version = "v1",
        Description = "Microservice for photo upload, storage, and management in dating app",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Dating App Team",
            Email = "dev@datingapp.com"
        }
    });

    // Include XML comments for better API documentation
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // JWT Bearer token support in Swagger
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme (Example: 'Bearer token')",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Database Configuration - PostgreSQL for all environments (superior for dating apps)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? 
                      Environment.GetEnvironmentVariable("DATABASE_URL") ??
                      "Host=localhost;Database=photos_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<PhotoContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null);
        // Enable PostGIS for geospatial queries (useful for location-based features)
        npgsqlOptions.UseNetTopologySuite();
    }));

// JWT Authentication - RSA Public Key Configuration to match AuthService
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// ImageSharp Configuration - Industry standard image processing
// Enable for all environments with PostgreSQL
builder.Services.AddImageSharp();

// Custom Services - Dependency Injection
builder.Services.AddScoped<IPhotoService, PhotoService.Services.PhotoService>();
builder.Services.AddScoped<IImageProcessingService, ImageProcessingService>();
builder.Services.AddScoped<IStorageService, LocalStorageService>();

// HTTP Context for URL generation
builder.Services.AddHttpContextAccessor();

// CORS Configuration - For cross-origin requests
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// HTTP Client for external service communication
builder.Services.AddHttpClient();

var app = builder.Build();

// ================================
// HTTP REQUEST PIPELINE CONFIGURATION
// Standard middleware pipeline for API security and functionality
// ================================

// Development vs Production middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Photo Service API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}
else
{
    // Production: Still enable Swagger for API documentation
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Photo Service API v1");
        c.RoutePrefix = "api-docs";
    });
}

// Security Headers - Production best practices
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    await next();
});

// Standard middleware pipeline
app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Static file serving for uploaded photos
app.UseStaticFiles();

// ImageSharp middleware for image processing
app.UseImageSharp();

// API Controllers
app.MapControllers();

// Health Check Endpoint - Standard for microservices
app.MapGet("/health", () => new { Status = "Healthy", Service = "PhotoService", Timestamp = DateTime.UtcNow });

// Database Migration on Startup - Development convenience
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<PhotoContext>();
        // Ensure database is created and migrated
        context.Database.EnsureCreated();
    }
}

// Force use environment URL if available
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (!string.IsNullOrEmpty(urls))
{
    app.Urls.Clear();
    app.Urls.Add(urls);
}

app.Run();

