using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the ski-coach capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>skicoachagenta2a</c> A2A agent
/// (<c>ski-coach-agent-a2a</c>) — it does not replace or modify it. <see cref="Description"/> and the skill
/// metadata below are reused from that agent's existing <c>AgentCard</c>. <see cref="BuildSkillMarkdown"/>
/// documents the live sibling resources exposed by <see cref="CoachSkillResources"/>. The generated content is
/// served exclusively over MCP; the orchestrator reads the definition and operational results as skill resources.
/// </remarks>
public static class CoachSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "ski-coach";

    /// <summary>
    /// The skill description, reused verbatim from <c>skicoachagenta2a</c>'s existing A2A <c>AgentCard</c> description.
    /// </summary>
    public const string Description = "Personalized ski slope recommendation and day planning agent for AlpineAI ski resort";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>skicoachagenta2a</c>'s existing agent instructions
    /// (<c>ski_coach_agent_python/agent_executor.py</c>).
    /// </summary>
    public const string Instructions =
        "You are the Ski Coach Agent for AlpineAI ski resort. You help skiers find the best slopes based on " +
        "their skill level, preferences, and current conditions. When users ask for recommendations, always ask " +
        "about their skill level if not provided (beginner, intermediate, advanced, expert). Read the " +
        "recommendations resource to get current conditions and recommendations. Read the day-plan resource to " +
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

    /// <summary>Builds the canonical <c>skill://ski-coach/SKILL.md</c> content.</summary>
    public static string BuildSkillMarkdown()
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
        builder.AppendLine("## Live resources");
        builder.AppendLine();
        builder.AppendLine(
            "Use `read_skill_resource` with one of the exact relative resource names below. " +
            "All operational behavior is available by reading these sibling resources.");
        builder.AppendLine();
        builder.AppendLine("| Relative resource name/template | Arguments | Description |");
        builder.AppendLine("|---|---|---|");
        builder.AppendLine("| `recommendations/{skillLevel}{?preferences}` | Required `skillLevel` path segment: `beginner`, `intermediate`, `advanced`, or `expert`. Optional `preferences` query value: comma-separated flags such as `avoid_crowds,groomed_only`. Example: `recommendations/intermediate?preferences=avoid_crowds%2Cgroomed_only`. | Personalized slope recommendations. |");
        builder.AppendLine("| `day-plan/{skillLevel}` | Required `skillLevel` path segment: `beginner`, `intermediate`, `advanced`, or `expert`. Example: `day-plan/beginner`. | Morning, midday, and afternoon ski plan. |");
        builder.AppendLine();
        builder.AppendLine("Percent-encode path and query values before placing them in a resource name.");

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
