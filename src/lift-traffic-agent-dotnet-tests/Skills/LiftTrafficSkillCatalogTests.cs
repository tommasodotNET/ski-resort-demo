using System.Net.Http;
using System.Text.Json;
using LiftTrafficAgent.Dotnet.Services;
using LiftTrafficAgent.Dotnet.Skills;
using LiftTrafficAgent.Dotnet.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiftTrafficAgent.Dotnet.Tests.Skills;

public class LiftTrafficSkillCatalogTests
{
    private static LiftTrafficTools CreateTools()
        => new(new LiftDataService(new StubHttpClientFactory(), NullLogger<LiftDataService>.Instance));

    [Fact]
    public void BuildIndexJson_DescribesTheLiftTrafficSkill()
    {
        var json = LiftTrafficSkillCatalog.BuildIndexJson();

        var document = JsonSerializer.Deserialize<LiftTrafficSkillCatalog.SkillIndexDocument>(json)
            ?? throw new InvalidOperationException("Failed to deserialize skill index document.");

        Assert.NotEmpty(document.Schema);
        var entry = Assert.Single(document.Skills);
        Assert.Equal(LiftTrafficSkillCatalog.SkillName, entry.Name);
        Assert.Equal("skill-md", entry.Type);
        Assert.Equal(LiftTrafficSkillCatalog.Description, entry.Description);
        Assert.Equal($"skill://{LiftTrafficSkillCatalog.SkillName}/SKILL.md", entry.Url);
    }

    [Fact]
    public void BuildSkillMarkdown_IncludesFrontMatterAndInstructions()
    {
        var markdown = LiftTrafficSkillCatalog.BuildSkillMarkdown(CreateTools().GetFunctions());

        Assert.Contains($"name: {LiftTrafficSkillCatalog.SkillName}", markdown);
        Assert.Contains($"description: {LiftTrafficSkillCatalog.Description}", markdown);
        Assert.Contains(LiftTrafficSkillCatalog.Instructions, markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ListsEveryToolAsAScript()
    {
        var tools = CreateTools().GetFunctions().ToList();

        var markdown = LiftTrafficSkillCatalog.BuildSkillMarkdown(tools);

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
