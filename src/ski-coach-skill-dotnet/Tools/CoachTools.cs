using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using SkiCoachSkill.Dotnet.Services;

namespace SkiCoachSkill.Dotnet.Tools;

/// <summary>
/// The ski-coach skill's tools ("scripts"), ported from <c>ski-coach-agent-python</c>'s
/// <c>tools/coach_tools.py</c>. Each method is registered as a literal MCP tool via
/// <see cref="McpServerToolAttribute"/> — the "script" a skills-based orchestrator invokes after discovering
/// this skill over MCP (see <c>Skills/CoachSkillResources.cs</c>).
/// </summary>
[McpServerToolType]
public class CoachTools
{
    private readonly CoachDataService _coachDataService;

    public CoachTools(CoachDataService coachDataService)
    {
        _coachDataService = coachDataService;
    }

    [McpServerTool(Name = "recommend_slope")]
    [Description("Get personalized slope recommendations based on skill level and preferences")]
    public async Task<string> RecommendSlope(
        [Description("Skier skill level: 'beginner', 'intermediate', 'advanced', or 'expert'")] string skillLevel,
        [Description("Optional comma-separated preferences like 'avoid_crowds,groomed_only'")] string? preferences = null)
    {
        return await _coachDataService.RecommendSlopeAsync(skillLevel, preferences);
    }

    [McpServerTool(Name = "build_day_plan")]
    [Description("Build a full day ski plan with morning, midday, and afternoon recommendations")]
    public async Task<string> BuildDayPlan(
        [Description("Skier skill level: 'beginner', 'intermediate', 'advanced', or 'expert'")] string skillLevel)
    {
        return await _coachDataService.BuildDayPlanAsync(skillLevel);
    }

    /// <summary>Used only to derive the SKILL.md "Scripts" table from the same [Description] metadata above.</summary>
    public IEnumerable<AIFunction> GetFunctions()
    {
        return
        [
            AIFunctionFactory.Create(RecommendSlope),
            AIFunctionFactory.Create(BuildDayPlan)
        ];
    }
}
