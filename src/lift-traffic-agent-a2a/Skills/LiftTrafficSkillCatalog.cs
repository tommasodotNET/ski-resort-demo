using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace LiftTrafficAgent.Dotnet.Skills;

/// <summary>
/// Single source of truth for how the Lift Traffic Agent describes itself as an MCP-hosted "skill".
/// </summary>
/// <remarks>
/// This is the bridge between the agent-as-tool world (A2A) and the skills world (MCP, SEP-2640):
/// <list type="bullet">
/// <item><description><see cref="Description"/> is the same text used for the agent's A2A <c>AgentCard</c> and
/// <c>AddAIAgent</c> registration in <c>Program.cs</c> — i.e. "agent descriptions become skill descriptions".</description></item>
/// <item><description><see cref="BuildSkillMarkdown"/> renders the agent's existing <see cref="AIFunction"/> tools
/// (see <c>Tools/LiftTrafficTools.cs</c>) as the skill's "Scripts" section — i.e. "agent tools become skill scripts".
/// The very same methods are also registered as literal MCP tools (via <c>[McpServerTool]</c>), so the scripts listed
/// here are directly callable by any MCP client, not just descriptive text.</description></item>
/// </list>
/// The generated content is served exclusively through <see cref="LiftTrafficSkillResources"/> over MCP; nothing here
/// is duplicated into an orchestrator project, satisfying the "skill definitions must be read via MCP, not local to
/// the orchestrator" requirement.
/// </remarks>
public static class LiftTrafficSkillCatalog
{
    /// <summary>The SEP-2640 skill name advertised in the discovery index and the SKILL.md front-matter.</summary>
    public const string SkillName = "lift-traffic";

    /// <summary>The Agent Framework / A2A agent name (matches <c>AddAIAgent("lifttrafficagenta2a", ...)</c> in Program.cs).</summary>
    public const string AgentName = "lifttrafficagenta2a";

    /// <summary>
    /// The agent's description. Reused verbatim for the A2A <c>AgentCard</c>, the <c>AddAIAgent</c> registration,
    /// and the MCP skill description/index entry.
    /// </summary>
    public const string Description = "Lift congestion and traffic intelligence agent";

    /// <summary>
    /// The agent's operating instructions. Reused verbatim for the <c>AddAIAgent</c> registration and the SKILL.md body.
    /// </summary>
    public const string Instructions =
        "You are the Lift Traffic Agent for AlpineAI ski resort. You provide real-time lift status, wait times, " +
        "and congestion analysis. Help skiers find the least crowded areas and plan efficient lift usage.";

    private static readonly JsonSerializerOptions IndexSerializerOptions = new() { WriteIndented = true };

    /// <summary>Builds the SEP-2640 <c>skill://index.json</c> discovery document for this agent's single skill.</summary>
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
    /// Builds the <c>SKILL.md</c> content for this agent, listing the supplied <paramref name="scripts"/>
    /// (the agent's existing <see cref="AIFunction"/> tools) as callable MCP tool "scripts".
    /// </summary>
    public static string BuildSkillMarkdown(IEnumerable<AIFunction> scripts)
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
        builder.AppendLine("## Scripts");
        builder.AppendLine();
        builder.AppendLine(
            "Each script below is also registered as a literal MCP tool on this server (same name). " +
            "Call the tool directly to get live lift data — there is no code to execute.");
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
