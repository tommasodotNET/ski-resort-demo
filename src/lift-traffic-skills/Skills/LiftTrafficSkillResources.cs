using System.ComponentModel;
using LiftTrafficSkill.Dotnet.Services;
using ModelContextProtocol.Server;

namespace LiftTrafficSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the lift-traffic capability as an SEP-2640 skill and live operational sibling resources.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;LiftTrafficSkillResources&gt;()</c> in <c>Program.cs</c>. The canonical
/// definition remains at <c>skill://lift-traffic/SKILL.md</c>; every live operation is a sibling beneath
/// <c>skill://lift-traffic/</c> so <c>MCPSkill.get_resource</c> can resolve it by relative name.
/// </remarks>
[McpServerResourceType]
public sealed class LiftTrafficSkillResources
{
    private readonly LiftDataService _liftDataService;

    public LiftTrafficSkillResources(LiftDataService liftDataService)
    {
        _liftDataService = liftDataService;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the lift-traffic skill")]
    public string GetIndex() => LiftTrafficSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://lift-traffic/SKILL.md", Name = "Lift Traffic Skill", MimeType = "text/markdown")]
    [Description("Lift traffic skill instructions and available live resources")]
    public string GetSkillMd() => LiftTrafficSkillCatalog.BuildSkillMarkdown();

    [McpServerResource(UriTemplate = "skill://lift-traffic/lifts", Name = "All Lifts", MimeType = "application/json")]
    [Description("List all resort lifts with status, queue length, and wait times")]
    public Task<string> GetAllLifts() => _liftDataService.GetAllLiftsAsync();

    [McpServerResource(UriTemplate = "skill://lift-traffic/lifts/{liftId}", Name = "Lift Status", MimeType = "application/json")]
    [Description("Get current status, queue length, and wait time for a specific lift")]
    public Task<string> GetLiftStatus(string liftId) => _liftDataService.GetLiftByIdAsync(liftId);

    [McpServerResource(UriTemplate = "skill://lift-traffic/wait-times", Name = "Lift Wait Times", MimeType = "application/json")]
    [Description("Get current wait times for all resort lifts")]
    public Task<string> GetWaitTimes() => _liftDataService.GetAllLiftsAsync();

    [McpServerResource(UriTemplate = "skill://lift-traffic/least-busy-area", Name = "Least Busy Area", MimeType = "application/json")]
    [Description("Suggest the least congested open lift area")]
    public Task<string> GetLeastBusyArea() => _liftDataService.SuggestLessBusyAreaAsync();
}
