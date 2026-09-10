using System.Text.Json;
using System.Text.Json.Nodes;

namespace SafetySkill.Dotnet.Services;

/// <summary>
/// Fetches resort data from the data-generator service and applies the safety risk-evaluation rule engine.
/// </summary>
/// <remarks>
/// This is a faithful .NET port of <c>safety-agent-a2a</c>'s <c>SafetyService</c>
/// (<c>services/safety_service.py</c>): same data-generator endpoints (<c>/api/weather</c>, <c>/api/safety</c>,
/// <c>/api/slopes</c>) and the same risk-scoring rule engine
/// (<see cref="CalculateRiskScore"/>, <see cref="GetRiskLevel"/>) and difficulty-based safety thresholds. The
/// existing Python A2A agent is left untouched; this service backs a new, additive MCP skill-provider server.
/// Upstream errors, malformed responses, and cancellation propagate to the MCP tool caller.
/// </remarks>
public class SafetyDataService
{
    private const string DataGeneratorUrl = "https+http://datagenerator";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, double> DifficultyThresholds = new Dictionary<string, double>
    {
        ["black"] = 0.5,
        ["red"] = 0.6,
        ["blue"] = 0.7,
        ["green"] = 0.8
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SafetyDataService> _logger;

    public SafetyDataService(IHttpClientFactory httpClientFactory, ILogger<SafetyDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _logger.LogInformation("SafetyDataService initialized with data-generator URL: {Url}", DataGeneratorUrl);
    }

    private HttpClient CreateDataGeneratorClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(DataGeneratorUrl);
        return httpClient;
    }

    private async Task<JsonObject> FetchWeatherAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();
            using var response = await httpClient.GetAsync("/api/weather", cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var weather = JsonNode.Parse(content) as JsonObject
                ?? throw new JsonException("Expected a weather object.");
            foreach (var field in new[] { "temperature", "wind_speed", "snow_intensity", "visibility" })
                _ = (weather[field] ?? throw new JsonException($"Missing weather field: {field}.")).GetValue<double>();
            return weather;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error fetching weather data");
            throw;
        }
    }

    private async Task<JsonObject> FetchSafetyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();
            using var response = await httpClient.GetAsync("/api/safety", cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var safety = JsonNode.Parse(content) as JsonObject
                ?? throw new JsonException("Expected a safety object.");
            _ = (safety["avalanche_risk_index"] ?? throw new JsonException("Missing avalanche risk index.")).GetValue<double>();
            if (safety["incident_reports"] is not JsonArray)
                throw new JsonException("Expected incident reports array.");
            return safety;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error fetching safety data");
            throw;
        }
    }

    private async Task<JsonArray> FetchSlopesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();
            using var response = await httpClient.GetAsync("/api/slopes", cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var slopes = JsonNode.Parse(content) as JsonArray
                ?? throw new JsonException("Expected a slopes array.");
            foreach (var slope in slopes)
            {
                if (slope is not JsonObject item)
                    throw new JsonException("Expected a slope object.");
                foreach (var field in new[] { "slope_id", "name", "difficulty" })
                    _ = (item[field] ?? throw new JsonException($"Missing slope field: {field}.")).GetValue<string>();
                _ = (item["is_open"] ?? throw new JsonException("Missing slope is_open flag.")).GetValue<bool>();
            }
            return slopes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error fetching slopes data");
            throw;
        }
    }

    /// <summary>
    /// Calculates a risk score (0-1, clamped) and the list of contributing factors, based on weather and safety
    /// data. Pure rule engine — no I/O — mirrors <c>SafetyService._calculate_risk_score</c> exactly.
    /// </summary>
    public static (double RiskScore, List<string> Factors) CalculateRiskScore(JsonObject weather, JsonObject safety)
    {
        var risk = safety["avalanche_risk_index"]?.GetValue<double>() ?? 0.0;
        var factors = new List<string>();

        var windSpeed = weather["wind_speed"]?.GetValue<double>() ?? 0;
        if (windSpeed > 50)
        {
            risk += 0.2;
            factors.Add($"Extreme wind speed: {windSpeed} km/h");
        }
        else if (windSpeed > 30)
        {
            risk += 0.1;
            factors.Add($"High wind speed: {windSpeed} km/h");
        }

        var visibility = weather["visibility"]?.GetValue<double>() ?? 5000;
        if (visibility < 500)
        {
            risk += 0.15;
            factors.Add($"Very low visibility: {visibility}m");
        }
        else if (visibility < 1000)
        {
            risk += 0.05;
            factors.Add($"Low visibility: {visibility}m");
        }

        var snowIntensity = weather["snow_intensity"]?.GetValue<double>() ?? 0;
        if (snowIntensity > 3)
        {
            risk += 0.1;
            factors.Add($"Heavy snowfall: intensity {snowIntensity}");
        }

        var avalancheRiskIndex = safety["avalanche_risk_index"]?.GetValue<double>() ?? 0;
        if (avalancheRiskIndex > 0)
        {
            factors.Add($"Avalanche risk index: {avalancheRiskIndex:F2}");
        }

        risk = Math.Max(0.0, Math.Min(1.0, risk));

        return (risk, factors);
    }

    /// <summary>Converts a risk score to a risk level string. Mirrors <c>SafetyService._get_risk_level</c>.</summary>
    public static string GetRiskLevel(double riskScore)
    {
        if (riskScore < 0.3)
        {
            return "low";
        }

        if (riskScore < 0.5)
        {
            return "moderate";
        }

        if (riskScore < 0.7)
        {
            return "high";
        }

        return "critical";
    }

    /// <summary>Evaluates risk for a specific area or resort-wide. Mirrors <c>SafetyService.evaluate_risk</c>.</summary>
    public async Task<string> EvaluateRiskAsync(string area, CancellationToken cancellationToken = default)
    {
        try
        {
            var weather = await FetchWeatherAsync(cancellationToken);
            var safety = await FetchSafetyAsync(cancellationToken);
            var slopes = await FetchSlopesAsync(cancellationToken);

            var (riskScore, factors) = CalculateRiskScore(weather, safety);
            var riskLevel = GetRiskLevel(riskScore);

            IEnumerable<JsonNode?> affectedSlopes = slopes;
            if (!string.IsNullOrEmpty(area) && !string.Equals(area, "all", StringComparison.OrdinalIgnoreCase))
            {
                var areaLower = area.ToLowerInvariant();
                affectedSlopes = slopes.Where(s =>
                    (s?["name"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant().Contains(areaLower));
            }

            var affectedSlopesJson = new JsonArray();
            foreach (var s in affectedSlopes)
            {
                affectedSlopesJson.Add(new JsonObject
                {
                    ["slope_id"] = s?["slope_id"]?.DeepClone(),
                    ["name"] = s?["name"]?.DeepClone(),
                    ["difficulty"] = s?["difficulty"]?.DeepClone(),
                    ["is_open"] = s?["is_open"]?.DeepClone()
                });
            }

            var result = new JsonObject
            {
                ["area"] = string.IsNullOrEmpty(area) ? "all" : area,
                ["risk_level"] = riskLevel,
                ["risk_score"] = Math.Round(riskScore, 2),
                ["factors"] = new JsonArray(factors.Select(f => (JsonNode)f).ToArray()),
                ["affected_slopes"] = affectedSlopesJson,
                ["weather"] = weather.DeepClone(),
                ["incident_reports"] = safety["incident_reports"]?.DeepClone() ?? new JsonArray()
            };

            return result.ToJsonString(SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error evaluating risk");
            throw;
        }
    }

    /// <summary>Checks if a specific slope is safe to ski on. Mirrors <c>SafetyService.is_slope_safe</c>.</summary>
    public async Task<string> IsSlopeSafeAsync(string slopeId, CancellationToken cancellationToken = default)
    {
        try
        {
            var weather = await FetchWeatherAsync(cancellationToken);
            var safety = await FetchSafetyAsync(cancellationToken);
            var slopes = await FetchSlopesAsync(cancellationToken);

            var slope = slopes.FirstOrDefault(s => s?["slope_id"]?.GetValue<string>() == slopeId) as JsonObject;

            if (slope is null)
            {
                return new JsonObject
                {
                    ["slope_id"] = slopeId,
                    ["is_safe"] = false,
                    ["risk_score"] = 1.0,
                    ["reasons"] = new JsonArray($"Slope {slopeId} not found")
                }.ToJsonString(SerializerOptions);
            }

            var (riskScore, factors) = CalculateRiskScore(weather, safety);

            var reasons = new List<string>();
            var isSafe = true;

            if (!(slope["is_open"]?.GetValue<bool>() ?? false))
            {
                isSafe = false;
                reasons.Add("Slope is currently closed");
            }

            var difficulty = (slope["difficulty"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant();
            var threshold = DifficultyThresholds.TryGetValue(difficulty, out var t) ? t : 0.7;

            if (riskScore > threshold)
            {
                isSafe = false;
                reasons.Add($"Risk level too high for {difficulty} slope: {riskScore:F2} (threshold: {threshold})");
            }

            reasons.AddRange(factors);

            var result = new JsonObject
            {
                ["slope_id"] = slopeId,
                ["slope_name"] = slope["name"]?.DeepClone(),
                ["difficulty"] = slope["difficulty"]?.DeepClone(),
                ["is_safe"] = isSafe,
                ["risk_score"] = Math.Round(riskScore, 2),
                ["reasons"] = reasons.Count > 0
                    ? new JsonArray(reasons.Select(r => (JsonNode)r).ToArray())
                    : new JsonArray("Slope is safe for skiing")
            };

            return result.ToJsonString(SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error checking slope safety");
            throw;
        }
    }

    /// <summary>Lists all currently closed slopes. Mirrors <c>SafetyService.get_closed_slopes</c>.</summary>
    public async Task<string> GetClosedSlopesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var slopes = await FetchSlopesAsync(cancellationToken);

            var closedSlopes = new JsonArray();
            var total = 0;
            foreach (var s in slopes)
            {
                if (s?["is_open"]?.GetValue<bool>() ?? false)
                {
                    continue;
                }

                total++;
                closedSlopes.Add(new JsonObject
                {
                    ["slope_id"] = s?["slope_id"]?.DeepClone(),
                    ["name"] = s?["name"]?.DeepClone(),
                    ["difficulty"] = s?["difficulty"]?.DeepClone(),
                    ["reasons"] = new JsonArray("Slope is closed by resort management")
                });
            }

            return new JsonObject
            {
                ["closed_slopes"] = closedSlopes,
                ["total_closed"] = total
            }.ToJsonString(SerializerOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error getting closed slopes");
            throw;
        }
    }
}
