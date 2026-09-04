using System.ComponentModel;
using ModelContextProtocol.Server;
using SafetySkill.Dotnet.Tools;

namespace SafetySkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the safety capability as an SEP-2640 "skill": a discovery index plus a SKILL.md
/// describing this server's tools as callable scripts.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;SafetySkillResources&gt;()</c> in <c>Program.cs</c> and served on the
/// same MCP endpoint (<c>/skillsmcp</c>) that hosts the tool "scripts" themselves (see <see cref="SafetyTools"/>,
/// registered via <c>.WithTools&lt;SafetyTools&gt;()</c>). A skills-based orchestrator discovers this skill
/// purely by connecting an MCP client to that endpoint — the skill definition never needs to live in the
/// orchestrator's own source tree.
/// </remarks>
[McpServerResourceType]
public sealed class SafetySkillResources
{
    private readonly SafetyTools _tools;

    public SafetySkillResources(SafetyTools tools)
    {
        _tools = tools;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the safety skill")]
    public string GetIndex() => SafetySkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://safety/SKILL.md", Name = "Safety Skill", MimeType = "text/markdown")]
    [Description("Safety skill instructions and available scripts")]
    public string GetSkillMd() => SafetySkillCatalog.BuildSkillMarkdown(_tools.GetFunctions());
}
