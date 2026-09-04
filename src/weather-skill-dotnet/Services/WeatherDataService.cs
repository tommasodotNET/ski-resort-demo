using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeatherSkill.Dotnet.Services;

/// <summary>
/// Fetches and derives weather data from the data-generator service.
/// </summary>
/// <remarks>
/// This is a faithful .NET port of <c>weather-agent-python</c>'s <c>WeatherService</c>
/// (<c>services/weather_service.py</c>): same data-generator endpoint (<c>/api/weather</c>), same fallback
/// values on failure, same forecast/storm-assessment rules. The existing Python A2A agent is left untouched;
/// this service backs a new, additive MCP skill-provider server (see <c>Tools/WeatherTools.cs</c>).
/// </remarks>
public class WeatherDataService
{
    private const string DataGeneratorUrl = "https+http://datagenerator";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeatherDataService> _logger;

    public WeatherDataService(IHttpClientFactory httpClientFactory, ILogger<WeatherDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _logger.LogInformation("WeatherDataService initialized with data-generator URL: {Url}", DataGeneratorUrl);
    }

    private HttpClient CreateDataGeneratorClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(DataGeneratorUrl);
        return httpClient;
    }

    /// <summary>
    /// Fetches current weather conditions as a parsed JSON object. Mirrors
    /// <c>WeatherService.get_current_conditions</c>: on failure, returns the same fallback shape
    /// (fixed temperature/wind/snow/visibility values plus an "error" field) instead of throwing.
    /// </summary>
    public async Task<JsonObject> GetCurrentConditionsAsync()
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();

            _logger.LogInformation("Fetching current weather conditions from {Url}/api/weather", DataGeneratorUrl);

            var response = await httpClient.GetAsync("/api/weather");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Retrieved weather data: {Content}", content);

            return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current weather conditions");
            return new JsonObject
            {
                ["temperature"] = -5.0,
                ["wind_speed"] = 15.0,
                ["snow_intensity"] = 1,
                ["visibility"] = 5000,
                ["timestamp"] = "unavailable",
                ["error"] = ex.Message
            };
        }
    }

    /// <summary>Same as <see cref="GetCurrentConditionsAsync"/>, serialized to indented JSON for tool output.</summary>
    public async Task<string> GetCurrentConditionsJsonAsync()
    {
        var conditions = await GetCurrentConditionsAsync();
        return conditions.ToJsonString(SerializerOptions);
    }

    /// <summary>
    /// Projects current conditions forward by <paramref name="hours"/> (clamped 1-24) with small random hourly
    /// variations. Mirrors <c>WeatherService.get_forecast</c> exactly, including the same variation ranges.
    /// </summary>
    public async Task<string> GetForecastAsync(int hours)
    {
        hours = Math.Clamp(hours, 1, 24);

        try
        {
            var current = await GetCurrentConditionsAsync();

            var baseTemp = current["temperature"]?.GetValue<double>() ?? -5.0;
            var baseWind = current["wind_speed"]?.GetValue<double>() ?? 15.0;
            var baseSnow = current["snow_intensity"]?.GetValue<double>() ?? 1;
            var baseVisibility = current["visibility"]?.GetValue<double>() ?? 5000;

            var hourlyForecast = new JsonArray();
            for (var hour = 1; hour <= hours; hour++)
            {
                var tempVariation = (Random.Shared.NextDouble() * 4) - 2; // uniform(-2, 2)
                var windVariation = (Random.Shared.NextDouble() * 10) - 5; // uniform(-5, 5)
                var snowVariation = Random.Shared.Next(-1, 2); // randint(-1, 1) inclusive
                var visibilityVariation = Random.Shared.Next(-500, 501); // randint(-500, 500) inclusive

                hourlyForecast.Add(new JsonObject
                {
                    ["hour"] = hour,
                    ["temperature"] = Math.Round(baseTemp + tempVariation, 1),
                    ["wind_speed"] = Math.Round(Math.Max(0, baseWind + windVariation), 1),
                    ["snow_intensity"] = Math.Max(0, Math.Min(5, baseSnow + snowVariation)),
                    ["visibility"] = Math.Max(100, baseVisibility + visibilityVariation)
                });
            }

            var result = new JsonObject
            {
                ["current_conditions"] = current.DeepClone(),
                ["forecast_hours"] = hours,
                ["hourly_forecast"] = hourlyForecast
            };

            return result.ToJsonString(SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating forecast");
            return new JsonObject
            {
                ["error"] = ex.Message,
                ["forecast_hours"] = hours,
                ["hourly_forecast"] = new JsonArray()
            }.ToJsonString(SerializerOptions);
        }
    }

    /// <summary>
    /// Assesses whether a storm is incoming, based on current conditions. Mirrors
    /// <c>WeatherService.is_storm_incoming</c> exactly, including its thresholds and wording.
    /// </summary>
    public async Task<string> IsStormIncomingAsync()
    {
        try
        {
            var current = await GetCurrentConditionsAsync();

            var windSpeed = current["wind_speed"]?.GetValue<double>() ?? 0;
            var snowIntensity = current["snow_intensity"]?.GetValue<double>() ?? 0;
            var visibility = current["visibility"]?.GetValue<double>() ?? 10000;

            var reasons = new List<string>();
            var stormIncoming = false;

            if (windSpeed > 50)
            {
                reasons.Add($"High wind speed detected: {windSpeed} km/h");
                stormIncoming = true;
            }

            if (snowIntensity > 3)
            {
                reasons.Add($"Heavy snow intensity: {snowIntensity}/5");
                stormIncoming = true;
            }

            if (visibility < 500)
            {
                reasons.Add($"Low visibility: {visibility}m");
                stormIncoming = true;
            }

            if (!stormIncoming)
            {
                if (windSpeed > 40)
                {
                    reasons.Add($"Elevated wind speed: {windSpeed} km/h");
                }

                if (snowIntensity >= 3)
                {
                    reasons.Add($"Moderate to heavy snow: {snowIntensity}/5");
                }

                if (visibility < 1000)
                {
                    reasons.Add($"Reduced visibility: {visibility}m");
                }
            }

            string reason;
            if (stormIncoming)
            {
                reason = "Storm conditions detected: " + string.Join("; ", reasons);
            }
            else if (reasons.Count > 0)
            {
                reason = "Monitoring conditions: " + string.Join("; ", reasons);
            }
            else
            {
                reason = $"Conditions are good (Wind: {windSpeed} km/h, Snow: {snowIntensity}/5, Visibility: {visibility}m)";
            }

            var result = new JsonObject
            {
                ["storm_incoming"] = stormIncoming,
                ["reason"] = reason,
                ["current_conditions"] = new JsonObject
                {
                    ["wind_speed"] = windSpeed,
                    ["snow_intensity"] = snowIntensity,
                    ["visibility"] = visibility
                }
            };

            return result.ToJsonString(SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assessing storm conditions");
            return new JsonObject
            {
                ["storm_incoming"] = false,
                ["reason"] = $"Unable to assess storm conditions: {ex.Message}",
                ["error"] = ex.Message
            }.ToJsonString(SerializerOptions);
        }
    }
}
