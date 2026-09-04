using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiftTrafficSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the lift-traffic capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>lifttrafficagenta2a</c> A2A agent
/// (<c>lift-traffic-agent-a2a</c>) — it does not replace or modify it. <see cref="Description"/> and
/// <see cref="Instructions"/> are reused verbatim from that agent's existing <c>AgentCard</c>/<c>AddAIAgent</c>
/// text (see <c>lift-traffic-agent-a2a/Skills/LiftTrafficSkillCatalog.cs</c>).
/// <see cref="BuildSkillMarkdown"/> documents the live sibling resources exposed by
/// <see cref="LiftTrafficSkillResources"/>. The generated content is served exclusively over MCP; the
/// orchestrator reads the definition and operational results as skill resources.
/// </remarks>
public static class LiftTrafficSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "lift-traffic";

    /// <summary>
    /// The skill description, reused verbatim from <c>lifttrafficagenta2a</c>'s existing agent description.
    /// </summary>
    public const string Description = "Lift congestion and traffic intelligence agent";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>lifttrafficagenta2a</c>'s existing
    /// <c>AddAIAgent</c> instructions.
    /// </summary>
    public const string Instructions =
        "You are the Lift Traffic Agent for AlpineAI ski resort. You provide real-time lift status, wait times, " +
        "and congestion analysis. Help skiers find the least crowded areas and plan efficient lift usage.";

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

    /// <summary>Builds the canonical <c>skill://lift-traffic/SKILL.md</c> content.</summary>
    public static string BuildSkillMarkdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Lift Traffic skill");
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
        builder.AppendLine("| `lifts` | None | All lifts with IDs, names, status, queue length, and wait times. Read this first to discover lift IDs. |");
        builder.AppendLine("| `lifts/{liftId}` | Required `liftId` path segment. Example: `lifts/chairlift-alpha`. | Current status of a specific lift. |");
        builder.AppendLine("| `wait-times` | None | Current wait times for all lifts. |");
        builder.AppendLine("| `least-busy-area` | None | Least congested open lift area. |");
        builder.AppendLine();
        builder.AppendLine("Percent-encode any path value before placing it in a resource name.");

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
