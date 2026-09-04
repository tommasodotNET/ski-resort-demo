using System.ComponentModel;
using LiftTrafficSkill.Dotnet.Services;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace LiftTrafficSkill.Dotnet.Tools;

/// <summary>
/// The lift-traffic skill's tools ("scripts"), registered as literal MCP tools on this standalone server.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>lift-traffic-agent-a2a</c> A2A agent
/// (<c>lift-traffic-agent-dotnet</c>) — it does not replace or modify it. The same tool logic is faithfully
/// reused here (see <c>Services/LiftDataService.cs</c>); <see cref="GetFunctions"/> exists purely to extract
/// the <see cref="DescriptionAttribute"/> metadata below for the SKILL.md "Scripts" table.
/// </remarks>
[McpServerToolType]
public class LiftTrafficTools
{
    private readonly LiftDataService _liftDataService;

    public LiftTrafficTools(LiftDataService liftDataService)
    {
        _liftDataService = liftDataService;
    }

    [McpServerTool(Name = "list_all_lifts")]
    [Description("List all ski lifts in the resort with their IDs, names, status, queue length, and wait times. Use this first to discover available lift IDs before querying a specific lift.")]
    public async Task<string> ListAllLifts()
    {
        return await _liftDataService.GetAllLiftsAsync();
    }

    [McpServerTool(Name = "get_lift_status")]
    [Description("Get the current status of a specific ski lift including wait time, queue length, and operational status")]
    public async Task<string> GetLiftStatus(
        [Description("The lift ID to check (e.g., 'chairlift-alpha', 'chairlift-bravo')")] string liftId)
    {
        return await _liftDataService.GetLiftByIdAsync(liftId);
    }

    [McpServerTool(Name = "get_wait_times")]
    [Description("Get current wait times for all ski lifts in the resort")]
    public async Task<string> GetWaitTimes()
    {
        return await _liftDataService.GetAllLiftsAsync();
    }

    [McpServerTool(Name = "suggest_less_busy_area")]
    [Description("Suggest the least congested area of the ski resort based on current lift wait times")]
    public async Task<string> SuggestLessBusyArea()
    {
        return await _liftDataService.SuggestLessBusyAreaAsync();
    }

    /// <summary>Used only to derive the SKILL.md "Scripts" table from the same [Description] metadata above.</summary>
    public IEnumerable<AIFunction> GetFunctions()
    {
        return
        [
            AIFunctionFactory.Create(ListAllLifts),
            AIFunctionFactory.Create(GetLiftStatus),
            AIFunctionFactory.Create(GetWaitTimes),
            AIFunctionFactory.Create(SuggestLessBusyArea)
        ];
    }
}
