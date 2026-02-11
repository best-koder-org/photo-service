namespace PhotoService.Services;

public interface ISafetyServiceClient
{
    Task<bool> IsBlockedAsync(string userId, string targetUserId);
}

public class SafetyServiceClient : ISafetyServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SafetyServiceClient> _logger;

    public SafetyServiceClient(HttpClient httpClient, ILogger<SafetyServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsBlockedAsync(string userId, string targetUserId)
    {
        try
        {
            // Safety service uses authenticated requests - would need to pass through the JWT token
            var response = await _httpClient.GetAsync($"/api/safety/is-blocked/{targetUserId}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check block status for target {TargetUserId}: {StatusCode}",
                    targetUserId, response.StatusCode);
                return false; // Fail open - allow photo access if safety service is down
            }

            var result = await response.Content.ReadFromJsonAsync<SafetyApiResponse<bool>>();
            return result?.Data ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking block status for target {TargetUserId}", targetUserId);
            return false; // Fail open - allow photo access if safety service is down
        }
    }
}

record SafetyApiResponse<T>(bool Success, T? Data, string? Error = null);
