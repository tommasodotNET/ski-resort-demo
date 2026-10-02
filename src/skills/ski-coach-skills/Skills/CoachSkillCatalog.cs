using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using SkiCoachSkill.Dotnet.Tools;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>Instructional discovery for the ski-coach skill and its native MAF progressive MCP tools.</summary>
public static class CoachSkillCatalog
{
    public const string SkillName = "ski-coach";

    // Matches the host's MCPStreamableHTTPTool.tool_name_prefix.
    public const string ProviderPrefix = "skicoach";

    public const string Description = "Personalized ski slope recommendation and day planning agent for AlpineAI ski resort";

    public const string Instructions =
        "You are the Ski Coach Agent for AlpineAI ski resort. You help skiers find the best slopes based on " +
        "their skill level, preferences, and current conditions. When users ask for recommendations, always ask " +
        "about their skill level if not provided (beginner, intermediate, advanced, expert). " +
        "Always be encouraging and helpful. Skiing should be fun and safe!";

    private static readonly JsonSerializerOptions IndexSerializerOptions = new() { WriteIndented = true };

    // Document the same remote names that WithTools registers; do not maintain a second operation list.
    public static IReadOnlyList<string> ToolNames { get; } = Array.AsReadOnly(typeof(CoachTools).GetMethods()
        .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
        .OfType<string>().Order(StringComparer.Ordinal).ToArray());

    public static string BuildIndexJson() => JsonSerializer.Serialize(
        new SkillIndexDocument(
            Schema: "https://schemas.agentskills.io/discovery/0.2.0/schema.json",
            Skills: [new SkillIndexEntry(SkillName, "skill-md", Description, $"skill://{SkillName}/SKILL.md")]),
        IndexSerializerOptions);

    public static string BuildSkillMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"name: {SkillName}");
        builder.AppendLine($"description: {Description}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(Instructions);
        builder.AppendLine();
        builder.AppendLine("## Native skill-scoped tool loading");
        builder.AppendLine($"1. A successful `load_skill('{SkillName}')` automatically makes ALL tools from this provider " +
            "available on the NEXT model iteration, together with their full descriptions and input schemas.");
        builder.AppendLine("2. On that next iteration, use those descriptions and input schemas to select and directly invoke " +
            "only the operations required for the request.");
        builder.AppendLine("Loading the skill does not execute any tools. No separate tool-loader calls or custom dispatcher " +
            "are needed; call the registered functions directly.");
        builder.AppendLine();
        builder.AppendLine("| Direct callable after skill loading |");
        builder.AppendLine("|---|");
        foreach (var name in ToolNames)
            builder.AppendLine($"| `{ProviderPrefix}_{name}` |");
        return builder.ToString();
    }

    public sealed record SkillIndexDocument(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("skills")] IReadOnlyList<SkillIndexEntry> Skills);

    public sealed record SkillIndexEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("url")] string Url);
}
