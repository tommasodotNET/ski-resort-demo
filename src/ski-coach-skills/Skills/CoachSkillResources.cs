using System.ComponentModel;
using ModelContextProtocol.Server;
using SkiCoachSkill.Dotnet.Services;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the ski-coach capability as an SEP-2640 skill and live operational sibling resources.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;CoachSkillResources&gt;()</c> in <c>Program.cs</c>. The canonical
/// definition remains at <c>skill://ski-coach/SKILL.md</c>; every live operation is a sibling beneath
/// <c>skill://ski-coach/</c> so <c>MCPSkill.get_resource</c> can resolve it by relative name.
/// </remarks>
[McpServerResourceType]
public sealed class CoachSkillResources
{
    private readonly CoachDataService _coachDataService;

    public CoachSkillResources(CoachDataService coachDataService)
    {
        _coachDataService = coachDataService;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the ski-coach skill")]
    public string GetIndex() => CoachSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://ski-coach/SKILL.md", Name = "Ski Coach Skill", MimeType = "text/markdown")]
    [Description("Ski coach skill instructions and available live resources")]
    public string GetSkillMd() => CoachSkillCatalog.BuildSkillMarkdown();

    [McpServerResource(UriTemplate = "skill://ski-coach/recommendations/{skillLevel}{?preferences}", Name = "Slope Recommendations", MimeType = "application/json")]
    [Description("Get personalized slope recommendations for a skill level and optional preferences")]
    public Task<string> GetRecommendations(string skillLevel, string? preferences = null) =>
        _coachDataService.RecommendSlopeAsync(skillLevel, preferences);

    [McpServerResource(UriTemplate = "skill://ski-coach/day-plan/{skillLevel}", Name = "Ski Day Plan", MimeType = "application/json")]
    [Description("Build a morning, midday, and afternoon ski plan for a skill level")]
    public Task<string> GetDayPlan(string skillLevel) => _coachDataService.BuildDayPlanAsync(skillLevel);
}
