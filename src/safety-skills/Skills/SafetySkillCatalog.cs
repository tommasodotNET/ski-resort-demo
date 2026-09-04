using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafetySkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the safety capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>safetyagenta2a</c> A2A agent
/// (<c>safety-agent-a2a</c>) — it does not replace or modify it. <see cref="Description"/> and the skill
/// metadata below are reused from that agent's existing <c>AgentCard</c>. <see cref="BuildSkillMarkdown"/>
/// documents the live sibling resources exposed by <see cref="SafetySkillResources"/>. The generated content is
/// served exclusively over MCP; the orchestrator reads the definition and operational results as skill resources.
/// </remarks>
public static class SafetySkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "safety";

    /// <summary>
    /// The skill description, reused verbatim from <c>safetyagenta2a</c>'s existing A2A <c>AgentCard</c> description.
    /// </summary>
    public const string Description = "Risk evaluation and slope safety agent for AlpineAI ski resort";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>safetyagenta2a</c>'s existing agent instructions
    /// (<c>safety_agent_python/agent_executor.py</c>).
    /// </summary>
    public const string Instructions =
        "You are the Safety Agent for AlpineAI ski resort. Your role is to evaluate risk across slopes using " +
        "weather, avalanche, and visibility data. Safety is your top priority. Always err on the side of caution. " +
        "Risk levels: Low (< 0.3) normal skiing conditions; Moderate (0.3-0.5) exercise caution; " +
        "High (0.5-0.7) dangerous for some slopes; Critical (>= 0.7) recommend resort closure. " +
        "When in doubt, recommend caution.";

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

    /// <summary>Builds the canonical <c>skill://safety/SKILL.md</c> content.</summary>
    public static string BuildSkillMarkdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Safety skill");
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
        builder.AppendLine("| `risk{?area}` | Optional `area` query value; omit it for `all`. Examples: `risk`, `risk?area=north-face`. | Safety risk for an area or the entire resort. |");
        builder.AppendLine("| `slopes/{slopeId}/safety` | Required `slopeId` path segment. Example: `slopes/valley-run/safety`. | Whether a specific slope is safe under current conditions. |");
        builder.AppendLine("| `closed-slopes` | None | All currently closed slopes and closure reasons. |");
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
