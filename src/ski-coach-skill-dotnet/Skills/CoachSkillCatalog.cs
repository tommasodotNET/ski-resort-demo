using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the ski-coach capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>skicoachagent</c> A2A agent
/// (<c>ski-coach-agent-python</c>) — it does not replace or modify it. <see cref="Description"/> and the skill
/// metadata below are reused verbatim from that agent's existing <c>AgentCard</c>
/// (<c>ski_coach_agent_python/main.py</c>'s <c>get_agent_card</c>), per "agent descriptions become skill
/// descriptions". <see cref="BuildSkillMarkdown"/> renders the same tools already registered as MCP tools
/// (see <c>Tools/CoachTools.cs</c>) as the skill's "Scripts" section, per "agent tools become skill scripts".
/// The generated content is served exclusively through <see cref="CoachSkillResources"/> over MCP — there is no
/// orchestrator project in this repository that duplicates this catalog, satisfying "skill definitions must be
/// read via MCP, not local to the orchestrator" (the orchestrator is a separate Python project).
/// </remarks>
public static class CoachSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "ski-coach";

    /// <summary>
    /// The skill description, reused verbatim from <c>skicoachagent</c>'s existing A2A <c>AgentCard</c> description.
    /// </summary>
    public const string Description = "Personalized ski slope recommendation and day planning agent for AlpineAI ski resort";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>skicoachagent</c>'s existing agent instructions
    /// (<c>ski_coach_agent_python/agent_executor.py</c>).
    /// </summary>
    public const string Instructions =
        "You are the Ski Coach Agent for AlpineAI ski resort. You help skiers find the best slopes based on " +
        "their skill level, preferences, and current conditions. When users ask for recommendations, always ask " +
        "about their skill level if not provided (beginner, intermediate, advanced, expert). Use the " +
        "recommend_slope tool to get current conditions and recommendations. Use the build_day_plan tool to " +
        "create a structured day schedule. Always be encouraging and helpful. Skiing should be fun and safe!";

    private static readonly JsonSerializerOptions IndexSerializerOptions = new() { WriteIndented = true };

    /// <summary>Builds the SEP-2640 <c>skill://index.json</c> discovery document for this skill.</summary>
    public static string BuildIndexJson()
    {
        var document = new SkillIndexDocument(
            Schema: "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
            Skills:
            [
                new SkillIndexEntry(
                    Name: SkillName,
                    Type: "skill-md",
                    Description: Description,
                    Url: $"skill://{SkillName}/SKILL.md")
            ]);

        return JsonSerializer.Serialize(document, IndexSerializerOptions);
    }

    /// <summary>
    /// Builds the <c>SKILL.md</c> content for this skill, listing the supplied <paramref name="scripts"/>
    /// (this server's MCP tools) as callable "scripts".
    /// </summary>
    public static string BuildSkillMarkdown(IEnumerable<AIFunction> scripts)
    {
        var builder = new StringBuilder();

        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Ski coach skill");
        builder.AppendLine();
        builder.AppendLine(Instructions);
        builder.AppendLine();
        builder.AppendLine("## Scripts");
        builder.AppendLine();
        builder.AppendLine(
            "Each script below is also registered as a literal MCP tool on this server (same name). " +
            "Call the tool directly to get live recommendations — there is no code to execute.");
        builder.AppendLine();
        builder.AppendLine("| Script | Description |");
        builder.AppendLine("|--------|-------------|");

        foreach (var script in scripts)
        {
            builder.AppendLine($"| `{script.Name}` | {script.Description} |");
        }

        return builder.ToString();
    }

    /// <summary>The parsed shape of <c>skill://index.json</c>, exposed for unit testing.</summary>
    public sealed record SkillIndexDocument(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("skills")] IReadOnlyList<SkillIndexEntry> Skills);

    /// <summary>A single entry in <see cref="SkillIndexDocument"/>, exposed for unit testing.</summary>
    public sealed record SkillIndexEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("url")] string Url);
}
