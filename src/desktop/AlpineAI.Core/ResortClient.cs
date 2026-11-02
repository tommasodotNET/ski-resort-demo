using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlpineAI.Core;

public sealed record Weather(double Temperature, double WindSpeed, double SnowIntensity, double Visibility);
public sealed record Incident(string IncidentType, string Location, string Severity, string? Timestamp);
public sealed record Safety(double AvalancheRiskIndex, Incident[] IncidentReports)
{
    [JsonIgnore]
    public string RiskLevel => AvalancheRiskIndex switch
    {
        < 0.2 => "Low",
        < 0.4 => "Moderate",
        < 0.6 => "Considerable",
        < 0.8 => "High",
        _ => "Extreme"
    };
}
public sealed record Lift(string LiftId, string Name, string Status, int QueueLength,
    double WaitTimeMinutes, string[] ServesSlopes);
public sealed record Slope(string SlopeId, string Name, string Difficulty, bool IsOpen,
    double SnowDepthCm, bool Groomed, string ServedByLiftId);
public sealed record AppConfig(int PollingIntervalMs);

public sealed class ResortClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task<Weather> GetWeatherAsync(CancellationToken token) => GetAsync<Weather>("weather", token);
    public Task<Safety> GetSafetyAsync(CancellationToken token) => GetAsync<Safety>("safety", token);
    public Task<Lift[]> GetLiftsAsync(CancellationToken token) => GetListAsync<Lift>("lifts", token);
    public Task<Slope[]> GetSlopesAsync(CancellationToken token) => GetListAsync<Slope>("slopes", token);

    public async Task<AppConfig> GetConfigAsync(CancellationToken token)
    {
        var config = await http.GetFromJsonAsync<AppConfig>("config.json", token)
            ?? throw new JsonException("The server returned an empty dashboard configuration.");
        if (config.PollingIntervalMs < 250 || config.PollingIntervalMs > 3_600_000)
            throw new JsonException("pollingIntervalMs must be between 250 and 3600000.");
        return config;
    }

    private async Task<T> GetAsync<T>(string resource, CancellationToken token) =>
        await http.GetFromJsonAsync<T>($"api/current-state/{resource}", Json, token)
        ?? throw new JsonException($"The server returned empty {resource} data.");

    private async Task<T[]> GetListAsync<T>(string resource, CancellationToken token)
    {
        var data = await GetAsync<JsonElement>(resource, token);
        if (data.ValueKind == JsonValueKind.Object)
            data = data.GetProperty(resource);
        return data.Deserialize<T[]>(Json) ?? throw new JsonException($"Invalid {resource} list.");
    }

    public static Uri ParseServerUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ALPINEAI_SERVER_URL is missing. Start the desktop resource from Aspire so its backend is configured automatically.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && !(uri.Scheme == "http"
                && (uri.IsLoopback || uri.DnsSafeHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))))
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0
            || uri.Fragment.Length != 0)
            throw new ArgumentException("ALPINEAI_SERVER_URL must be an HTTPS origin or an HTTP localhost origin. Check the desktop resource configuration in Aspire.");
        return uri;
    }
}
