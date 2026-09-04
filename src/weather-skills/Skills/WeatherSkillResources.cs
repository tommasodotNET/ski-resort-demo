using System.ComponentModel;
using ModelContextProtocol.Server;
using WeatherSkill.Dotnet.Services;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the weather capability as an SEP-2640 skill and live operational sibling resources.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;WeatherSkillResources&gt;()</c> in <c>Program.cs</c>. The canonical
/// definition remains at <c>skill://weather/SKILL.md</c>; every live operation is a sibling beneath
/// <c>skill://weather/</c> so <c>MCPSkill.get_resource</c> can resolve it by relative name.
/// </remarks>
[McpServerResourceType]
public sealed class WeatherSkillResources
{
    private readonly WeatherDataService _weatherDataService;

    public WeatherSkillResources(WeatherDataService weatherDataService)
    {
        _weatherDataService = weatherDataService;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the weather skill")]
    public string GetIndex() => WeatherSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://weather/SKILL.md", Name = "Weather Skill", MimeType = "text/markdown")]
    [Description("Weather skill instructions and available live resources")]
    public string GetSkillMd() => WeatherSkillCatalog.BuildSkillMarkdown();

    [McpServerResource(UriTemplate = "skill://weather/current-conditions", Name = "Current Weather Conditions", MimeType = "application/json")]
    [Description("Get current resort temperature, wind speed, snow intensity, and visibility")]
    public Task<string> GetCurrentConditions() => _weatherDataService.GetCurrentConditionsJsonAsync();

    [McpServerResource(UriTemplate = "skill://weather/forecast/{hours}", Name = "Weather Forecast", MimeType = "application/json")]
    [Description("Get a resort weather forecast for a number of hours from 1 through 24")]
    public Task<string> GetForecast(int hours) => _weatherDataService.GetForecastAsync(hours);

    [McpServerResource(UriTemplate = "skill://weather/storm-status", Name = "Storm Status", MimeType = "application/json")]
    [Description("Assess whether a storm is incoming from current resort conditions")]
    public Task<string> GetStormStatus() => _weatherDataService.IsStormIncomingAsync();
}
