using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;

namespace PhotoService.Services;

/// <summary>
/// Background service that polls for AUTO_APPROVED voice prompts
/// and runs them through the moderation pipeline.
///
/// Flow:
///   Upload → entity created with status=AUTO_APPROVED → served immediately
///   This service picks it up → transcribe → moderate → APPROVED or REJECTED
///   If REJECTED, ServeAudioForUser() already filters it out (existing query).
///
/// Polling interval is configurable; defaults to every 30 seconds.
/// In production, consider replacing with a message queue (RabbitMQ/Redis Streams).
/// </summary>
public class VoicePromptModerationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoicePromptModerationBackgroundService> _logger;
    private readonly IConfiguration _configuration;

    public VoicePromptModerationBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<VoicePromptModerationBackgroundService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("VoiceModeration:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("Voice prompt moderation is disabled in configuration");
            return;
        }

        var pollIntervalSeconds = _configuration.GetValue("VoiceModeration:PollIntervalSeconds", 30);
        var pollInterval = TimeSpan.FromSeconds(pollIntervalSeconds);

        _logger.LogInformation(
            "Voice Prompt Moderation Background Service started (poll every {Interval}s)",
            pollIntervalSeconds);

        // Initial delay to let app startup complete
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingPromptsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in voice prompt moderation polling loop");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }

        _logger.LogInformation("Voice Prompt Moderation Background Service stopped");
    }

    private async Task ProcessPendingPromptsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PhotoContext>();
        var moderationService = scope.ServiceProvider.GetRequiredService<IVoicePromptModerationService>();

        // Find voice prompts that are AUTO_APPROVED (not yet moderated)
        var pendingIds = await context.VoicePrompts
            .Where(v => !v.IsDeleted && v.ModerationStatus == ModerationStatus.AutoApproved)
            .OrderBy(v => v.CreatedAt)
            .Select(v => v.Id)
            .Take(10) // batch of 10 per cycle
            .ToListAsync(ct);

        if (pendingIds.Count == 0) return;

        _logger.LogInformation("Found {Count} voice prompts pending moderation", pendingIds.Count);

        foreach (var id in pendingIds)
        {
            if (ct.IsCancellationRequested) break;

            var result = await moderationService.ModerateAsync(id, ct);
            if (result.Success)
            {
                _logger.LogInformation("Moderated voice prompt {Id}: {Status}", id, result.FinalStatus);
            }
            else
            {
                _logger.LogWarning("Failed to moderate voice prompt {Id}: {Error}", id, result.ErrorMessage);
            }
        }
    }
}
