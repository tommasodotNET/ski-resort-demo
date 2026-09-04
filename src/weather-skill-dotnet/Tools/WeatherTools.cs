using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using WeatherSkill.Dotnet.Services;

namespace WeatherSkill.Dotnet.Tools;

/// <summary>
/// The weather skill's tools ("scripts"), ported from <c>weather-agent-python</c>'s
/// <c>tools/weather_tools.py</c>. Each method is registered as a literal MCP tool via
/// <see cref="McpServerToolAttribute"/> — the "script" a skills-based orchestrator invokes after discovering
/// this skill over MCP (see <c>Skills/WeatherSkillResources.cs</c>).
/// </summary>
[McpServerToolType]
public class WeatherTools
{
    private readonly WeatherDataService _weatherDataService;

    public WeatherTools(WeatherDataService weatherDataService)
    {
        _weatherDataService = weatherDataService;
    }

    [McpServerTool(Name = "get_current_conditions")]
    [Description("Get current weather conditions at the ski resort including temperature, wind speed, snow intensity, and visibility")]
    public async Task<string> GetCurrentConditions()
    {
        return await _weatherDataService.GetCurrentConditionsJsonAsync();
    }

    [McpServerTool(Name = "get_forecast")]
    [Description("Get a weather forecast for the specified number of hours ahead (1-24)")]
    public async Task<string> GetForecast(
        [Description("Number of hours to forecast, 1-24")] int hours = 6)
    {
        return await _weatherDataService.GetForecastAsync(hours);
    }

    [McpServerTool(Name = "is_storm_incoming")]
    [Description("Assess whether a storm is incoming based on current weather conditions")]
    public async Task<string> IsStormIncoming()
    {
        return await _weatherDataService.IsStormIncomingAsync();
    }

    /// <summary>Used only to derive the SKILL.md "Scripts" table from the same [Description] metadata above.</summary>
    public IEnumerable<AIFunction> GetFunctions()
    {
        return
        [
            AIFunctionFactory.Create(GetCurrentConditions),
            AIFunctionFactory.Create(GetForecast),
            AIFunctionFactory.Create(IsStormIncoming)
        ];
    }
}
