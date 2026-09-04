using System.ComponentModel;
using ModelContextProtocol.Server;
using SafetySkill.Dotnet.Services;

namespace SafetySkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the safety capability as an SEP-2640 skill and live operational sibling resources.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;SafetySkillResources&gt;()</c> in <c>Program.cs</c>. The canonical
/// definition remains at <c>skill://safety/SKILL.md</c>; every live operation is a sibling beneath
/// <c>skill://safety/</c> so <c>MCPSkill.get_resource</c> can resolve it by relative name.
/// </remarks>
[McpServerResourceType]
public sealed class SafetySkillResources
{
    private readonly SafetyDataService _safetyDataService;

    public SafetySkillResources(SafetyDataService safetyDataService)
    {
        _safetyDataService = safetyDataService;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the safety skill")]
    public string GetIndex() => SafetySkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://safety/SKILL.md", Name = "Safety Skill", MimeType = "text/markdown")]
    [Description("Safety skill instructions and available live resources")]
    public string GetSkillMd() => SafetySkillCatalog.BuildSkillMarkdown();

    [McpServerResource(UriTemplate = "skill://safety/risk{?area}", Name = "Safety Risk", MimeType = "application/json")]
    [Description("Evaluate safety risk for a specific area or the entire resort")]
    public Task<string> EvaluateRisk(string area = "all") => _safetyDataService.EvaluateRiskAsync(area);

    [McpServerResource(UriTemplate = "skill://safety/slopes/{slopeId}/safety", Name = "Slope Safety", MimeType = "application/json")]
    [Description("Check whether a specific slope is safe under current conditions")]
    public Task<string> GetSlopeSafety(string slopeId) => _safetyDataService.IsSlopeSafeAsync(slopeId);

    [McpServerResource(UriTemplate = "skill://safety/closed-slopes", Name = "Closed Slopes", MimeType = "application/json")]
    [Description("Get all currently closed slopes and their closure reasons")]
    public Task<string> GetClosedSlopes() => _safetyDataService.GetClosedSlopesAsync();
}
