using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SafetySkill.Dotnet.Skills;

/// <summary>Instructional skill discovery only; safety operations are typed MCP tools.</summary>
[McpServerResourceType]
public sealed class SafetySkillResources
{
    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the safety skill")]
    public string GetIndex() => SafetySkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://safety/SKILL.md", Name = "Safety Skill", MimeType = "text/markdown")]
    [Description("Safety instructions and native progressive tool-loading guidance")]
    public string GetSkillMd() => SafetySkillCatalog.BuildSkillMarkdown();
}
