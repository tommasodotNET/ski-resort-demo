using System.ComponentModel;
using LiftTrafficSkill.Dotnet.Tools;
using ModelContextProtocol.Server;

namespace LiftTrafficSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the lift-traffic capability as an SEP-2640 "skill": a discovery index plus a SKILL.md
/// describing this server's tools as callable scripts.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;LiftTrafficSkillResources&gt;()</c> in <c>Program.cs</c> and served on the
/// same MCP endpoint (<c>/skillsmcp</c>) that hosts the tool "scripts" themselves (see
/// <see cref="LiftTrafficTools"/>, registered via <c>.WithTools&lt;LiftTrafficTools&gt;()</c>). A skills-based
/// orchestrator discovers this skill purely by connecting an MCP client to that endpoint — the skill definition
/// never needs to live in the orchestrator's own source tree.
/// </remarks>
[McpServerResourceType]
public sealed class LiftTrafficSkillResources
{
    private readonly LiftTrafficTools _tools;

    public LiftTrafficSkillResources(LiftTrafficTools tools)
    {
        _tools = tools;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the lift-traffic skill")]
    public string GetIndex() => LiftTrafficSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://lift-traffic/SKILL.md", Name = "Lift Traffic Skill", MimeType = "text/markdown")]
    [Description("Lift traffic skill instructions and available scripts")]
    public string GetSkillMd() => LiftTrafficSkillCatalog.BuildSkillMarkdown(_tools.GetFunctions());
}
