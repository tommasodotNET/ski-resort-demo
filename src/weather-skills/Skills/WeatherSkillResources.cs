using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>Instructional skill discovery only; weather operations are typed MCP tools.</summary>
[McpServerResourceType]
public sealed class WeatherSkillResources
{
    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the weather skill")]
    public string GetIndex() => WeatherSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://weather/SKILL.md", Name = "Weather Skill", MimeType = "text/markdown")]
    [Description("Weather instructions and native progressive tool-loading guidance")]
    public string GetSkillMd() => WeatherSkillCatalog.BuildSkillMarkdown();
}
