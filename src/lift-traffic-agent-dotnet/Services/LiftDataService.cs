using System.Text.Json;

namespace LiftTrafficAgent.Dotnet.Services;

public class LiftDataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LiftDataService> _logger;
    private readonly string _dataGeneratorUrl;

    public LiftDataService(IHttpClientFactory httpClientFactory, ILogger<LiftDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        
        // Get data-generator URL from Aspire service discovery
        _dataGeneratorUrl = Environment.GetEnvironmentVariable("services__data-generator__http__0")
            ?? throw new InvalidOperationException("services__data-generator__http__0 not found in environment variables");
        
        _logger.LogInformation("LiftDataService initialized with data-generator URL: {Url}", _dataGeneratorUrl);
    }

    public async Task<string> GetAllLiftsAsync()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_dataGeneratorUrl);
            
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
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_dataGeneratorUrl);
            
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
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_dataGeneratorUrl);
            
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
