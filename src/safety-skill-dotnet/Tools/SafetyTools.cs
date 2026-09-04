using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using SafetySkill.Dotnet.Services;

namespace SafetySkill.Dotnet.Tools;

/// <summary>
/// The safety skill's tools ("scripts"), ported from <c>safety-agent-python</c>'s <c>tools/safety_tools.py</c>.
/// Each method is registered as a literal MCP tool via <see cref="McpServerToolAttribute"/> — the "script" a
/// skills-based orchestrator invokes after discovering this skill over MCP (see
/// <c>Skills/SafetySkillResources.cs</c>).
/// </summary>
[McpServerToolType]
public class SafetyTools
{
    private readonly SafetyDataService _safetyDataService;

    public SafetyTools(SafetyDataService safetyDataService)
    {
        _safetyDataService = safetyDataService;
    }

    [McpServerTool(Name = "evaluate_risk")]
    [Description("Evaluate safety risk for a specific area or the entire resort")]
    public async Task<string> EvaluateRisk(
        [Description("Area or zone name to evaluate risk for. Use 'all' for resort-wide assessment.")] string area = "all")
    {
        return await _safetyDataService.EvaluateRiskAsync(area);
    }

    [McpServerTool(Name = "is_slope_safe")]
    [Description("Check if a specific slope is safe to ski on based on current conditions")]
    public async Task<string> IsSlopeSafe(
        [Description("The slope ID to check safety for (e.g., 'valley-run', 'north-face')")] string slopeId)
    {
        return await _safetyDataService.IsSlopeSafeAsync(slopeId);
    }

    [McpServerTool(Name = "get_closed_slopes")]
    [Description("Get a list of all currently closed slopes with reasons for closure")]
    public async Task<string> GetClosedSlopes()
    {
        return await _safetyDataService.GetClosedSlopesAsync();
    }

    /// <summary>Used only to derive the SKILL.md "Scripts" table from the same [Description] metadata above.</summary>
    public IEnumerable<AIFunction> GetFunctions()
    {
        return
        [
            AIFunctionFactory.Create(EvaluateRisk),
            AIFunctionFactory.Create(IsSlopeSafe),
            AIFunctionFactory.Create(GetClosedSlopes)
        ];
    }
}
