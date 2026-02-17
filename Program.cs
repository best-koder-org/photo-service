using DatingApp.Shared.Middleware;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Extensions;
using PhotoService.Services;
using PhotoService.Common;
using PhotoService.Common;
using SixLabors.ImageSharp.Web.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithCorrelationId()
    .Enrich.WithProperty("ServiceName", "PhotoService")
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/photo-service-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ServiceName}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
    ));

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

// Database Configuration - MySQL for consistency across all services
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ??
                      Environment.GetEnvironmentVariable("DATABASE_URL") ??
                      "Server=localhost;Port=3311;Database=PhotoServiceDb;User=photoservice_user;Password=photoservice_user_password;";

var serverVersion = new MySqlServerVersion(new Version(8, 0, 32));
builder.Services.AddDbContext<PhotoContext>(options =>
    options.UseMySql(connectionString, serverVersion, mySqlOptions =>
    {
        mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
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

// Background Services - Periodic cleanup and maintenance
builder.Services.AddHostedService<PhotoCleanupBackgroundService>();

// Face Verification - DeepFace integration (DX-2: T155/T156)
builder.Services.AddDbContext<VerificationDbContext>(options =>
    options.UseMySql(connectionString, serverVersion, mySqlOptions =>
    {
        mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null);
    }));
builder.Services.AddScoped<IFaceVerificationService, FaceVerificationService>();
builder.Services.AddHttpClient("DeepFace", client =>
{
    client.BaseAddress = new Uri("http://deepface:5000");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Add MediatR for CQRS
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Add FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// HTTP Context for URL generation
builder.Services.AddHttpContextAccessor();
builder.Services.AddCorrelationIds();

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

// Internal API Key Authentication for service-to-service calls
builder.Services.AddScoped<InternalApiKeyAuthFilter>();
builder.Services.AddTransient<InternalApiKeyAuthHandler>();

// HTTP Client for external service communication
builder.Services.AddHttpClient();

// Safety Service Client for blocking checks
builder.Services.AddHttpClient<ISafetyServiceClient, SafetyServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://dejting-yarp:8080");
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>(); // Add internal API key to requests

// Matchmaking Service Client for match verification
builder.Services.AddHttpClient<IMatchmakingServiceClient, MatchmakingServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Gateway:BaseUrl"] ?? "http://dejting-yarp:8080");
})
.AddHttpMessageHandler<InternalApiKeyAuthHandler>(); // Add internal API key to requests

// Configure OpenTelemetry for metrics and distributed tracing
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "photo-service",
                    serviceVersion: "1.0.0"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("PhotoService")
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = (httpContext) =>
            {
                // Don't trace health checks and metrics endpoints
                var path = httpContext.Request.Path.ToString();
                return !path.Contains("/health") && !path.Contains("/metrics");
            };
        })
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.SetTag("db.query", command.CommandText);
            };
        }));

// Create custom meters for business metrics

// Register injectable business metrics
builder.Services.AddSingleton<PhotoService.Metrics.PhotoServiceMetrics>();

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
app.UseCorrelationIds();
app.UseAuthentication();
app.UseAuthorization();


// ImageSharp middleware for image processing
app.UseImageSharp();

// API Controllers
app.MapControllers();

// Health check now handled by HealthController

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics");

// Database Migration on Startup - Development convenience
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<PhotoContext>();
        var verificationContext = scope.ServiceProvider.GetRequiredService<VerificationDbContext>();
        // Ensure database is created and migrated
        context.Database.EnsureCreated();
        verificationContext.Database.EnsureCreated();
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

