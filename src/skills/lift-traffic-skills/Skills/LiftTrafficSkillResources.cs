using System.ComponentModel;
using ModelContextProtocol.Server;

namespace LiftTrafficSkill.Dotnet.Skills;

/// <summary>Instructional skill discovery only; lift operations are typed MCP tools.</summary>
[McpServerResourceType]
public sealed class LiftTrafficSkillResources
{
    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the lift-traffic skill")]
    public string GetIndex() => LiftTrafficSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://lift-traffic/SKILL.md", Name = "Lift Traffic Skill", MimeType = "text/markdown")]
    [Description("Lift-traffic instructions and native progressive tool-loading guidance")]
    public string GetSkillMd() => LiftTrafficSkillCatalog.BuildSkillMarkdown();
}
