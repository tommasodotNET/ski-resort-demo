using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SkiCoachSkill.Dotnet.Skills;

/// <summary>Instructional skill discovery only; coaching operations are typed MCP tools.</summary>
[McpServerResourceType]
public sealed class CoachSkillResources
{
    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the ski-coach skill")]
    public string GetIndex() => CoachSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://ski-coach/SKILL.md", Name = "Ski Coach Skill", MimeType = "text/markdown")]
    [Description("Ski-coach instructions and native progressive tool-loading guidance")]
    public string GetSkillMd() => CoachSkillCatalog.BuildSkillMarkdown();
}
