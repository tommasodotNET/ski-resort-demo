using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using WeatherSkill.Dotnet.Tools;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>Instructional discovery for the weather skill and its native MAF progressive MCP tools.</summary>
public static class WeatherSkillCatalog
{
    public const string SkillName = "weather";

    // Matches the host's MCPStreamableHTTPTool.tool_name_prefix.
    public const string ProviderPrefix = "weather";

    public const string Description =
        "Weather intelligence agent providing real-time conditions, forecasts, and storm alerts for the ski resort";

    public const string Instructions =
        "You are the Weather Intelligence Agent for AlpineAI ski resort. Your role is to help skiers, staff, and " +
        "resort operators understand current weather conditions, upcoming forecasts, and potential storm threats. " +
        "When users ask questions, always provide specific numbers and actionable recommendations. Be concise but " +
        "thorough. Safety is the top priority.";

    private static readonly JsonSerializerOptions IndexSerializerOptions = new() { WriteIndented = true };

    // Document the same remote names that WithTools registers; do not maintain a second operation list.
    public static IReadOnlyList<string> ToolNames { get; } = Array.AsReadOnly(typeof(WeatherTools).GetMethods()
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
        builder.AppendLine("## Native progressive tool loading");
        builder.AppendLine($"1. Use `load_skill` for `{SkillName}` to load these instructions.");
        builder.AppendLine($"2. Select only the operations needed for the request. Call `{ProviderPrefix}_load_tool` with " +
            "`{\"tool\":\"<remote tool name>\"}` or `{\"tool\":[\"<remote tool name>\",\"<another remote tool name>\"]}`.");
        builder.AppendLine("3. On the NEXT model iteration, call the directly registered tool using its newly disclosed input schema. " +
            "Loading a tool does not execute it; do not request loading and execution in the same iteration.");
        builder.AppendLine("Pass original remote names to the loader, not prefixed callable names. " +
            "Load named operations rather than listing the entire provider catalog.");
        builder.AppendLine();
        builder.AppendLine("| Remote tool name for loader | Direct callable after loading |");
        builder.AppendLine("|---|---|");
        foreach (var name in ToolNames)
            builder.AppendLine($"| `{name}` | `{ProviderPrefix}_{name}` |");
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
