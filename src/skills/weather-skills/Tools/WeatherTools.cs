using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Server;
using WeatherSkill.Dotnet.Services;

namespace WeatherSkill.Dotnet.Tools;

/// <summary>Typed, read-only adapters over reusable weather domain rules.</summary>
[McpServerToolType]
public sealed class WeatherTools(WeatherDataService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };

    [McpServerTool(Name = "weather_current_conditions", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Read current temperature, wind speed, snow intensity, visibility, and timestamp.")]
    public async Task<CurrentConditions> CurrentConditions(CancellationToken cancellationToken) =>
        Read<CurrentConditions>(await service.GetCurrentConditionsJsonAsync(cancellationToken));

    [McpServerTool(Name = "weather_forecast", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Generate a demo hourly forecast from current conditions. Forecast variation is randomized.")]
    public async Task<WeatherForecast> Forecast(
        [Description("Forecast horizon in hours, from 1 through 24."), Range(1, 24)] int hours,
        CancellationToken cancellationToken)
    {
        if (hours is < 1 or > 24)
            throw new ArgumentOutOfRangeException(nameof(hours), "Hours must be between 1 and 24.");
        return Read<WeatherForecast>(await service.GetForecastAsync(hours, cancellationToken));
    }

    [McpServerTool(Name = "weather_storm_status", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Assess storm conditions using live wind, snowfall, and visibility.")]
    public async Task<StormStatus> StormStatus(CancellationToken cancellationToken) =>
        Read<StormStatus>(await service.IsStormIncomingAsync(cancellationToken));

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("Missing weather result.");
}

public sealed record CurrentConditions(double temperature, double wind_speed, double snow_intensity, double visibility, string timestamp);
public sealed record ForecastHour(int hour, double temperature, double wind_speed, double snow_intensity, double visibility);
public sealed record WeatherForecast(CurrentConditions current_conditions, int forecast_hours, ForecastHour[] hourly_forecast);
public sealed record StormConditions(double wind_speed, double snow_intensity, double visibility);
public sealed record StormStatus(bool storm_incoming, string reason, StormConditions current_conditions);
