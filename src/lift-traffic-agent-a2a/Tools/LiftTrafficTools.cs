using System.ComponentModel;
using Microsoft.Extensions.AI;
using LiftTrafficAgent.Dotnet.Services;
using ModelContextProtocol.Server;

namespace LiftTrafficAgent.Dotnet.Tools;

/// <summary>
/// The Lift Traffic Agent's tools.
/// </summary>
/// <remarks>
/// Each method below is dual-hosted: <see cref="GetFunctions"/> exposes it as an <see cref="AIFunction"/> for the
/// agent's own A2A-facing <c>ChatClientAgent</c> (see <c>Program.cs</c>'s <c>AddAIAgent(...).AddA2AServer()</c>),
/// while the <see cref="McpServerToolAttribute"/> on the same method exposes it as a literal MCP tool — the
/// "script" a skills-based orchestrator invokes after discovering this agent's skill over MCP (see
/// <c>Skills/LiftTrafficSkillResources.cs</c>). There is exactly one implementation per capability; only the
/// hosting surface differs.
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
