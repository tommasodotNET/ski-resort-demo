using System.ComponentModel;
using ModelContextProtocol.Server;
using WeatherSkill.Dotnet.Tools;

namespace WeatherSkill.Dotnet.Skills;

/// <summary>
/// MCP resources exposing the weather capability as an SEP-2640 "skill": a discovery index plus a SKILL.md
/// describing this server's tools as callable scripts.
/// </summary>
/// <remarks>
/// Registered via <c>.WithResources&lt;WeatherSkillResources&gt;()</c> in <c>Program.cs</c> and served on the
/// same MCP endpoint (<c>/skillsmcp</c>) that hosts the tool "scripts" themselves (see <see cref="WeatherTools"/>,
/// registered via <c>.WithTools&lt;WeatherTools&gt;()</c>). A skills-based orchestrator discovers this skill
/// purely by connecting an MCP client to that endpoint — the skill definition never needs to live in the
/// orchestrator's own source tree.
/// </remarks>
[McpServerResourceType]
public sealed class WeatherSkillResources
{
    private readonly WeatherTools _tools;

    public WeatherSkillResources(WeatherTools tools)
    {
        _tools = tools;
    }

    [McpServerResource(UriTemplate = "skill://index.json", Name = "Skill Index", MimeType = "application/json")]
    [Description("SEP-2640 skill discovery index for the weather skill")]
    public string GetIndex() => WeatherSkillCatalog.BuildIndexJson();

    [McpServerResource(UriTemplate = "skill://weather/SKILL.md", Name = "Weather Skill", MimeType = "text/markdown")]
    [Description("Weather skill instructions and available scripts")]
    public string GetSkillMd() => WeatherSkillCatalog.BuildSkillMarkdown(_tools.GetFunctions());
}
