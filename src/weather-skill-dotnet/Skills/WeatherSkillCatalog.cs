using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how this MCP server describes the weather capability as a skill.
/// </summary>
/// <remarks>
/// This is a standalone MCP skill-provider counterpart to the existing <c>weatheragent</c> A2A agent
/// (<c>weather-agent-python</c>) — it does not replace or modify it. <see cref="Description"/> and the skill
/// metadata below are reused verbatim from that agent's existing <c>AgentCard</c>
/// (<c>weather_agent_python/main.py</c>'s <c>get_agent_card</c>), per "agent descriptions become skill
/// descriptions". <see cref="BuildSkillMarkdown"/> renders the same tools already registered as MCP tools
/// (see <c>Tools/WeatherTools.cs</c>) as the skill's "Scripts" section, per "agent tools become skill scripts".
/// The generated content is served exclusively through <see cref="WeatherSkillResources"/> over MCP — there is no
/// orchestrator project in this repository that duplicates this catalog, satisfying "skill definitions must be
/// read via MCP, not local to the orchestrator" (the orchestrator is a separate Python project).
/// </remarks>
public static class WeatherSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "weather";

    /// <summary>
    /// The skill description, reused verbatim from <c>weatheragent</c>'s existing A2A <c>AgentCard</c> description.
    /// </summary>
    public const string Description =
        "Weather intelligence agent providing real-time conditions, forecasts, and storm alerts for the ski resort";

    /// <summary>
    /// The skill's operating instructions, reused verbatim from <c>weatheragent</c>'s existing agent instructions
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
        builder.AppendLine("# Weather skill");
        builder.AppendLine();
        builder.AppendLine(Instructions);
        builder.AppendLine();
        builder.AppendLine("## Scripts");
        builder.AppendLine();
        builder.AppendLine(
            "Each script below is also registered as a literal MCP tool on this server (same name). " +
            "Call the tool directly to get live weather data — there is no code to execute.");
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
