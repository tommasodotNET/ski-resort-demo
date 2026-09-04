using System.ComponentModel;
using ModelContextProtocol.Server;
using SkiResearcherSkill.Dotnet.Tools;

namespace SkiResearcherSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the Ski Researcher Agent as an SEP-2640 "skill": a discovery index plus a SKILL.md
/// describing its single callable script.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;SkiResearcherSkillResources&gt;()</c> in <c>Program.cs</c> and served on
/// the same MCP endpoint (<c>/skillsmcp</c>) that hosts the <c>ask_ski_researcher</c> tool itself (see
/// <see cref="SkiResearcherTools"/>). A skills-based orchestrator discovers this agent purely by connecting an
/// MCP client to that endpoint — the skill definition never needs to live in the orchestrator's own source tree.
/// </remarks>
[McpServerResourceType]
public sealed class SkiResearcherSkillResources
{
    private readonly SkiResearcherTools _tools;

    public SkiResearcherSkillResources(SkiResearcherTools tools)
    {
        _tools = tools;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the Ski Researcher Agent")]
    public string GetIndex() => SkiResearcherSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://ski-researcher/SKILL.md", Name = "Ski Researcher Skill", MimeType = "text/markdown")]
    [Description("Ski researcher skill instructions and available scripts")]
    public string GetSkillMd() => SkiResearcherSkillCatalog.BuildSkillMarkdown(_tools.GetFunctions());
}
