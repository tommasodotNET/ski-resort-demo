using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace SkiResearcherSkill.Dotnet.Skills;

/// <summary>
/// Single source of truth for how the Ski Researcher Agent describes itself as an MCP-hosted "skill".
/// </summary>
/// <remarks>
/// Bridges the agent-as-tool world (the existing <c>ski-researcher-agent-a2a</c> Foundry prompt agent, wired
/// as a tool in <c>ski-advisor-a2a/Program.cs</c>) and the skills world (MCP, SEP-2640):
/// <see cref="Description"/> is the same text used to describe the agent when it is wired as an
/// <c>AIFunction</c> tool elsewhere ("agent descriptions become skill descriptions"), and
/// <see cref="BuildSkillMarkdown"/> renders the agent's single callable action
/// (<c>Tools/SkiResearcherTools.cs</c>'s <c>ask_ski_researcher</c>) as the skill's "Scripts" section
/// ("agent tools become skill scripts"). The generated content is served exclusively through
/// <see cref="SkiResearcherSkillResources"/> over MCP — nothing here is duplicated into an orchestrator
/// project.
/// </remarks>
public static class SkiResearcherSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "ski-researcher";

    /// <summary>Matches the description used when this agent is wired as an <c>AIFunction</c> tool elsewhere.</summary>
    public const string Description = "I can search the web. Use me for any generic question about skiing.";

    /// <summary>Mirrors the Foundry prompt agent's own instructions (see <c>apphost.cs</c>'s <c>AddPromptAgent</c> call).</summary>
    public const string Instructions =
        "You are a ski researcher agent. Your job is to research and provide information about ski. " +
        "Use the ask_ski_researcher script for any generic, non-resort-specific skiing question " +
        "(technique, gear, history, other destinations) that requires a web search to answer accurately. " +
        "Do not rely on internal knowledge alone for questions that may have changed over time.";

    public static string BuildIndexJson()
    {
        var document = new SkillIndexDocument(
            Schema: "sep-2640/skill-index-v1",
            Skills:
            [
                new SkillIndexEntry(
                    Name: SkillName,
                    Type: "skill-md",
                    Description: Description,
                    Url: $"skill://{SkillName}/SKILL.md")
            ]);

        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string BuildSkillMarkdown(IEnumerable<AIFunction> scripts)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# {SkillName}");
        builder.AppendLine();
        builder.AppendLine(Instructions);
        builder.AppendLine();
        builder.AppendLine("## Scripts");
        builder.AppendLine();

        foreach (var script in scripts)
        {
            builder.AppendLine($"### `{script.Name}`");
            builder.AppendLine();
            builder.AppendLine(script.Description);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>The top-level SEP-2640 discovery index document. Exposed for unit testing.</summary>
    public sealed record SkillIndexDocument(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("skills")] IReadOnlyList<SkillIndexEntry> Skills);

    /// <summary>A single entry in <see cref="SkillIndexDocument"/>, exposed for unit testing.</summary>
    public sealed record SkillIndexEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("url")] string Url);
}
