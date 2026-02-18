# ================================
# PHOTO SERVICE DOCKERFILE
# .NET 8 Web API for photo upload and management
# Optimized for production deployment with multi-stage build
# ================================

# Build stage - Use SDK image for compilation
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["PhotoService.csproj", "."]
RUN dotnet restore "PhotoService.csproj"

# Copy source code and build application
COPY . .
RUN dotnet build "PhotoService.csproj" -c Release -o /app/build

# Publish stage - Create optimized release build
FROM build AS publish
RUN dotnet publish "PhotoService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage - Use lightweight runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final

# ================================
# PRODUCTION CONFIGURATION
# Security and optimization settings
# ================================

# Install ffmpeg for Whisper.net audio conversion (m4a → WAV)
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r photoservice && useradd -r -g photoservice photoservice

# Set working directory
WORKDIR /app

# Create directories for photo storage
RUN mkdir -p /app/wwwroot/uploads/photos && \
    chown -R photoservice:photoservice /app

# Create directories for voice prompt uploads and Whisper model cache
RUN mkdir -p /app/uploads/voice-prompts /app/models && \
    chown -R photoservice:photoservice /app/uploads /app/models

# Copy published application
COPY --from=publish /app/publish .

# Set ownership for application files (including voice-prompts + models dirs)
RUN chown -R photoservice:photoservice /app

# ================================
# RUNTIME CONFIGURATION
# Environment and startup settings
# ================================

# Switch to non-root user
USER photoservice

# Configure ASP.NET Core
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8084

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8084/health || exit 1

# Expose port
EXPOSE 8084

# ================================
# APPLICATION STARTUP
# Define entry point for container
# ================================

ENTRYPOINT ["dotnet", "PhotoService.dll"]

# Build command:
# docker build -t dating-app/photo-service .

# Run command:
# docker run -d -p 8084:8084 --name photo-service \
#   -e ConnectionStrings__DefaultConnection="Server=mysql;Database=DatingApp_Photos;Uid=root;Pwd=password123;" \
#   -e JwtSettings__SecretKey="your-production-secret-key" \
#   dating-app/photo-service
