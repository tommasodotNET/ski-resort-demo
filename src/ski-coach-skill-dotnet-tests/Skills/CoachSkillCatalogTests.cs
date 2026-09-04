using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SkiCoachSkill.Dotnet.Services;
using SkiCoachSkill.Dotnet.Skills;
using SkiCoachSkill.Dotnet.Tools;

namespace SkiCoachSkill.Dotnet.Tests.Skills;

public class CoachSkillCatalogTests
{
    private static CoachTools CreateTools()
        => new(new CoachDataService(new StubHttpClientFactory(), NullLogger<CoachDataService>.Instance));

    [Fact]
    public void BuildIndexJson_DescribesTheCoachSkill()
    {
        var json = CoachSkillCatalog.BuildIndexJson();

        var document = JsonSerializer.Deserialize<CoachSkillCatalog.SkillIndexDocument>(json)
            ?? throw new InvalidOperationException("Failed to deserialize skill index document.");

        Assert.NotEmpty(document.Schema);
        var entry = Assert.Single(document.Skills);
        Assert.Equal(CoachSkillCatalog.SkillName, entry.Name);
        Assert.Equal("skill-md", entry.Type);
        Assert.Equal(CoachSkillCatalog.Description, entry.Description);
        Assert.Equal($"skill://{CoachSkillCatalog.SkillName}/SKILL.md", entry.Url);
    }

    [Fact]
    public void BuildSkillMarkdown_IncludesFrontMatterAndInstructions()
    {
        var markdown = CoachSkillCatalog.BuildSkillMarkdown(CreateTools().GetFunctions());

        Assert.Contains($"name: {CoachSkillCatalog.SkillName}", markdown);
        Assert.Contains($"description: {CoachSkillCatalog.Description}", markdown);
        Assert.Contains(CoachSkillCatalog.Instructions, markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ListsEveryToolAsAScript()
    {
        var tools = CreateTools().GetFunctions().ToList();

        var markdown = CoachSkillCatalog.BuildSkillMarkdown(tools);

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
