using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WeatherSkill.Dotnet.Services;
using WeatherSkill.Dotnet.Skills;
using WeatherSkill.Dotnet.Tools;

namespace WeatherSkill.Dotnet.Tests.Skills;

public class WeatherSkillCatalogTests
{
    private static WeatherTools CreateTools()
        => new(new WeatherDataService(new StubHttpClientFactory(), NullLogger<WeatherDataService>.Instance));

    [Fact]
    public void BuildIndexJson_DescribesTheWeatherSkill()
    {
        var json = WeatherSkillCatalog.BuildIndexJson();

        var document = JsonSerializer.Deserialize<WeatherSkillCatalog.SkillIndexDocument>(json)
            ?? throw new InvalidOperationException("Failed to deserialize skill index document.");

        Assert.NotEmpty(document.Schema);
        var entry = Assert.Single(document.Skills);
        Assert.Equal(WeatherSkillCatalog.SkillName, entry.Name);
        Assert.Equal("skill-md", entry.Type);
        Assert.Equal(WeatherSkillCatalog.Description, entry.Description);
        Assert.Equal($"skill://{WeatherSkillCatalog.SkillName}/SKILL.md", entry.Url);
    }

    [Fact]
    public void BuildSkillMarkdown_IncludesFrontMatterAndInstructions()
    {
        var markdown = WeatherSkillCatalog.BuildSkillMarkdown(CreateTools().GetFunctions());

        Assert.Contains($"name: {WeatherSkillCatalog.SkillName}", markdown);
        Assert.Contains($"description: {WeatherSkillCatalog.Description}", markdown);
        Assert.Contains(WeatherSkillCatalog.Instructions, markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ListsEveryToolAsAScript()
    {
        var tools = CreateTools().GetFunctions().ToList();

        var markdown = WeatherSkillCatalog.BuildSkillMarkdown(tools);

        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            Assert.Contains($"`{tool.Name}`", markdown);
            Assert.Contains(tool.Description, markdown);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
