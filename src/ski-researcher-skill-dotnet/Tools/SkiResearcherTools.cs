using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace SkiResearcherSkill.Dotnet.Tools;

/// <summary>
/// MCP tool wrapper around the existing <c>ski-researcher-agent-a2a</c> Foundry prompt agent (see
/// <c>apphost.cs</c>'s <c>project.AddPromptAgent("ski-researcher-agent-a2a", ...)</c>).
/// </summary>
/// <remarks>
/// Unlike the other four specialists, the ski researcher is not a locally-hosted A2A agent with its own
/// business-logic tools — it is a single Foundry-hosted prompt agent whose only "tool" is itself (it calls
/// Bing web search internally via <c>.WithTool(webSearch)</c>). "Agent tools become skill scripts" therefore
/// collapses to a single script here: asking the underlying agent a question and returning its answer.
/// The same <see cref="AIAgent"/> instance backs both <see cref="GetFunctions"/> (metadata, for SKILL.md
/// rendering) and the literal <c>[McpServerTool]</c> below (for actual invocation) — there is no separate
/// data service to fake or duplicate.
/// </remarks>
[McpServerToolType]
public class SkiResearcherTools
{
    private readonly AIAgent _agent;

    public SkiResearcherTools(AIAgent agent)
    {
        _agent = agent;
    }

    [McpServerTool(Name = "ask_ski_researcher")]
    [Description("Searches the web for general skiing questions and returns a researched answer. Use for generic ski-related questions that are not resort-specific (e.g. ski technique, gear advice, ski history, or ski destinations elsewhere).")]
    public async Task<string> AskSkiResearcherAsync([Description("The skiing-related question to research")] string question)
    {
        var response = await _agent.RunAsync(question);
        return response.Text;
    }

    /// <summary>Exposes the same tool as an <see cref="AIFunction"/> purely for SKILL.md metadata rendering.</summary>
    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(
            AskSkiResearcherAsync,
            name: "ask_ski_researcher",
            description: "Searches the web for general skiing questions and returns a researched answer. Use for generic ski-related questions that are not resort-specific (e.g. ski technique, gear advice, ski history, or ski destinations elsewhere).");
    }
}
