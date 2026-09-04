using System.Text.Json;

namespace LiftTrafficSkill.Dotnet.Services;

/// <summary>
/// Fetches lift data from the data-generator service.
/// </summary>
/// <remarks>
/// This is a faithful .NET port of the lift-data logic already used by <c>lift-traffic-agent-dotnet</c>'s
/// <c>LiftDataService</c> (same data-generator endpoints, same fallback/error shapes). That existing A2A agent
/// (Aspire resource <c>lift-traffic-agent-a2a</c>) is left untouched; this is a separate, standalone MCP
/// skill-provider server (Aspire resource <c>lift-traffic-agent-skill</c>) — a dedicated project, not the same
/// binary as the A2A resource, mirroring the pattern already used for weather/safety/ski-coach/ski-researcher.
/// </remarks>
public class LiftDataService
{
    private const string DataGeneratorUrl = "https+http://datagenerator";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiftDataService> _logger;
    private readonly string _dataGeneratorUrl;

    public LiftDataService(IHttpClientFactory httpClientFactory, ILogger<LiftDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dataGeneratorUrl = DataGeneratorUrl;

        _logger.LogInformation("LiftDataService initialized with data-generator URL: {Url}", _dataGeneratorUrl);
    }

    private HttpClient CreateDataGeneratorClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(_dataGeneratorUrl);
        return httpClient;
    }

    public async Task<string> GetAllLiftsAsync()
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();

            _logger.LogInformation("Fetching all lifts from {Url}/api/lifts", _dataGeneratorUrl);

            var response = await httpClient.GetAsync("/api/lifts");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Retrieved lift data: {Content}", content);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all lifts data");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    public async Task<string> GetLiftByIdAsync(string liftId)
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();

            _logger.LogInformation("Fetching lift {LiftId} from {Url}/api/lifts/{LiftId}", liftId, _dataGeneratorUrl, liftId);

            var response = await httpClient.GetAsync($"/api/lifts/{liftId}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Retrieved lift data for {LiftId}: {Content}", liftId, content);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching lift {LiftId} data", liftId);
            return JsonSerializer.Serialize(new { error = ex.Message, liftId });
        }
    }

    public async Task<string> SuggestLessBusyAreaAsync()
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();

            _logger.LogInformation("Fetching all lifts to determine least busy area");

            var response = await httpClient.GetAsync("/api/lifts");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var lifts = JsonSerializer.Deserialize<JsonElement>(content);

            // Find the open lift with the shortest wait time
            string? bestLiftId = null;
            string? bestLiftName = null;
            double minWaitTime = double.MaxValue;

            foreach (var lift in lifts.EnumerateArray())
            {
                var status = lift.GetProperty("status").GetString();
                if (status == "open")
                {
                    var waitTime = lift.GetProperty("wait_time_minutes").GetDouble();
                    if (waitTime < minWaitTime)
                    {
                        minWaitTime = waitTime;
                        bestLiftId = lift.GetProperty("lift_id").GetString();
                        bestLiftName = lift.GetProperty("name").GetString();
                    }
                }
            }

            if (bestLiftId == null)
            {
                return JsonSerializer.Serialize(new
                {
                    recommendation = "No open lifts available at this time",
                    waitTime = 0
                });
            }

            var recommendation = new
            {
                recommendation = $"Head to {bestLiftName} (Lift {bestLiftId}) - shortest wait time",
                liftId = bestLiftId,
                liftName = bestLiftName,
                waitTimeMinutes = minWaitTime
            };

            _logger.LogInformation("Recommendation: {LiftName} with {WaitTime} minutes wait", bestLiftName, minWaitTime);

            return JsonSerializer.Serialize(recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suggesting less busy area");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
