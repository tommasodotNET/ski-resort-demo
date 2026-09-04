using System.ComponentModel;
using ModelContextProtocol.Server;
using SkiCoachSkill.Dotnet.Tools;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the ski-coach capability as an SEP-2640 "skill": a discovery index plus a SKILL.md
/// describing this server's tools as callable scripts.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;CoachSkillResources&gt;()</c> in <c>Program.cs</c> and served on the
/// same MCP endpoint (<c>/skillsmcp</c>) that hosts the tool "scripts" themselves (see <see cref="CoachTools"/>,
/// registered via <c>.WithTools&lt;CoachTools&gt;()</c>). A skills-based orchestrator discovers this skill
/// purely by connecting an MCP client to that endpoint — the skill definition never needs to live in the
/// orchestrator's own source tree.
/// </remarks>
[McpServerResourceType]
public sealed class CoachSkillResources
{
    private readonly CoachTools _tools;

    public CoachSkillResources(CoachTools tools)
    {
        _tools = tools;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the ski-coach skill")]
    public string GetIndex() => CoachSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://ski-coach/SKILL.md", Name = "Ski Coach Skill", MimeType = "text/markdown")]
    [Description("Ski coach skill instructions and available scripts")]
    public string GetSkillMd() => CoachSkillCatalog.BuildSkillMarkdown(_tools.GetFunctions());
}
