namespace PhotoService.Services;

public interface IMatchmakingServiceClient
{
    Task<bool> AreUsersMatchedAsync(string userId1, string userId2);
}

public class MatchmakingServiceClient : IMatchmakingServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MatchmakingServiceClient> _logger;

    public MatchmakingServiceClient(HttpClient httpClient, ILogger<MatchmakingServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> AreUsersMatchedAsync(string userId1, string userId2)
    {
        try
        {
            _logger.LogDebug("Checking match status between {UserId1} and {UserId2}", userId1, userId2);
            
            // Call matchmaking service to check if users are matched
            // GET /api/matchmaking/matches/{userId} returns all matches for a user
            var response = await _httpClient.GetAsync($"/api/matchmaking/matches/{userId1}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check match status for user {UserId1}: {StatusCode}", 
                    userId1, response.StatusCode);
                return false; // Fail secure - require match if service is down
            }

            var result = await response.Content.ReadFromJsonAsync<MatchesResponse>();
            
            if (result?.Matches == null)
            {
                return false;
            }

            // Check if userId2 is in the matched users list
            var isMatched = result.Matches.Any(m => m.MatchedUserId.ToString() == userId2);
            
            _logger.LogDebug("Match check result: {IsMatched} for users {UserId1} and {UserId2}", 
                isMatched, userId1, userId2);
            
            return isMatched;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking match status between {UserId1} and {UserId2}", 
                userId1, userId2);
            return false; // Fail secure - require match on error
        }
    }
}

// Response DTOs matching MatchmakingService API
record MatchesResponse(List<MatchDto> Matches, int TotalCount, int ActiveCount);
record MatchDto(int MatchId, int MatchedUserId, DateTime MatchedAt, double? CompatibilityScore);
