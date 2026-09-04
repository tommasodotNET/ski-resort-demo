using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the weather capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>weatheragenta2a</c> A2A agent
/// (<c>weather-agent-a2a</c>) — it does not replace or modify it. <see cref="Description"/> and the skill
/// metadata below are reused from that agent's existing <c>AgentCard</c>. <see cref="BuildSkillMarkdown"/>
/// documents the live sibling resources exposed by <see cref="WeatherSkillResources"/>. The generated content is
/// served exclusively over MCP; the orchestrator reads the definition and operational results as skill resources.
/// </remarks>
public static class WeatherSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "weather";

    /// <summary>
    /// The skill description, reused verbatim from <c>weatheragenta2a</c>'s existing A2A <c>AgentCard</c> description.
    /// </summary>
    public const string Description =
        "Weather intelligence agent providing real-time conditions, forecasts, and storm alerts for the ski resort";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>weatheragenta2a</c>'s existing agent instructions
    /// (<c>weather_agent_python/agent_executor.py</c>).
    /// </summary>
    public const string Instructions =
        "You are the Weather Intelligence Agent for AlpineAI ski resort. Your role is to help skiers, staff, and " +
        "resort operators understand current weather conditions, upcoming forecasts, and potential storm threats. " +
        "When users ask questions, always provide specific numbers and actionable recommendations. Be concise but " +
        "thorough. Safety is the top priority.";

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

    /// <summary>Builds the canonical <c>skill://weather/SKILL.md</c> content.</summary>
    public static string BuildSkillMarkdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("# Weather skill");
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
        builder.AppendLine("| `current-conditions` | None | Current temperature, wind, snow intensity, and visibility. |");
        builder.AppendLine("| `forecast/{hours}` | `hours`: integer path segment; values are clamped to 1-24. Example: `forecast/6`. | Weather forecast for the requested number of hours. |");
        builder.AppendLine("| `storm-status` | None | Current storm assessment and contributing conditions. |");
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
