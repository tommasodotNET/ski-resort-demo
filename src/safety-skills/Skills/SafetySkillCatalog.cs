using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using SafetySkill.Dotnet.Tools;

namespace SafetySkill.Dotnet.Skills;

/// <summary>Instructional discovery for the safety skill and its native MAF progressive MCP tools.</summary>
public static class SafetySkillCatalog
{
    public const string SkillName = "safety";

    // Matches the host's MCPStreamableHTTPTool.tool_name_prefix.
    public const string ProviderPrefix = "safety";

    public const string Description = "Risk evaluation and slope safety agent for AlpineAI ski resort";

    public const string Instructions =
        "You are the Safety Agent for AlpineAI ski resort. Your role is to evaluate risk across slopes using " +
        "weather, avalanche, and visibility data. Safety is your top priority. Always err on the side of caution. " +
        "Risk levels: Low (< 0.3) normal skiing conditions; Moderate (0.3-0.5) exercise caution; " +
        "High (0.5-0.7) dangerous for some slopes; Critical (>= 0.7) recommend resort closure. " +
        "When in doubt, recommend caution.";

    private static readonly JsonSerializerOptions IndexSerializerOptions = new() { WriteIndented = true };

    // Document the same remote names that WithTools registers; do not maintain a second operation list.
    public static IReadOnlyList<string> ToolNames { get; } = Array.AsReadOnly(typeof(SafetyTools).GetMethods()
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
