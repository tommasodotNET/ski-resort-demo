using System.Text.RegularExpressions;
using Azure.AI.VoiceLive;
using Microsoft.Agents.AI;

namespace VoiceAdvisorAgent;

/// <summary>
/// Extension methods to convert MAF A2A agents into Voice Live function tool definitions.
/// </summary>
public static partial class VoiceLiveAgentExtensions
{
    private static readonly BinaryData s_queryParameters = BinaryData.FromObjectAsJson(new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description = "Input query to invoke the agent"
            }
        },
        required = new[] { "query" }
    });

    /// <summary>
    /// Converts an A2A <see cref="AIAgent"/> into a <see cref="VoiceLiveFunctionDefinition"/>
    /// that can be registered as a tool on a Voice Live session.
    /// </summary>
    public static VoiceLiveFunctionDefinition AsVoiceLiveTool(this AIAgent agent, string fallbackName)
    {
        // The registry key is the stable public contract used by the prompt and by
        // function-call lookup. Agent-card names are not necessarily snake_case and
        // some wrappers do not expose a name at all.
        var toolName = SanitizeAgentName(fallbackName)
            ?? SanitizeAgentName(agent.Name)
            ?? throw new InvalidOperationException(
                "Voice Live tool name cannot be empty. Provide a non-empty agent name or registry key.");

        return new VoiceLiveFunctionDefinition(toolName)
        {
            Description = agent.Description ?? $"Invoke the {toolName} agent",
            Parameters = s_queryParameters
        };
    }

    private static string? SanitizeAgentName(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        var sanitizedName = InvalidNameCharsRegex().Replace(agentName, "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitizedName) ? null : sanitizedName;
    }

    [GeneratedRegex("[^0-9A-Za-z]+")]
    private static partial Regex InvalidNameCharsRegex();
}
