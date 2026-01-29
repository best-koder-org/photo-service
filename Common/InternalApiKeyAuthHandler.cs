namespace PhotoService.Common;

/// <summary>
/// Delegating handler that adds internal API key to outgoing service-to-service requests
/// </summary>
public class InternalApiKeyAuthHandler : DelegatingHandler
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalApiKeyAuthHandler> _logger;

    public InternalApiKeyAuthHandler(IConfiguration configuration, ILogger<InternalApiKeyAuthHandler> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _configuration["InternalAuth:ApiKey"];
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-Internal-API-Key", apiKey);
            _logger.LogDebug("Added internal API key to request to {Uri}", request.RequestUri);
        }
        else
        {
            _logger.LogWarning("Internal API key not configured for service-to-service auth");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
