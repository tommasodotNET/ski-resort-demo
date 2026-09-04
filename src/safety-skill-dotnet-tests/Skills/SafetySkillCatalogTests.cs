using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SafetySkill.Dotnet.Services;
using SafetySkill.Dotnet.Skills;
using SafetySkill.Dotnet.Tools;

namespace SafetySkill.Dotnet.Tests.Skills;

public class SafetySkillCatalogTests
{
    private static SafetyTools CreateTools()
        => new(new SafetyDataService(new StubHttpClientFactory(), NullLogger<SafetyDataService>.Instance));

    [Fact]
    public void BuildIndexJson_DescribesTheSafetySkill()
    {
        var json = SafetySkillCatalog.BuildIndexJson();

        var document = JsonSerializer.Deserialize<SafetySkillCatalog.SkillIndexDocument>(json)
            ?? throw new InvalidOperationException("Failed to deserialize skill index document.");

        Assert.NotEmpty(document.Schema);
        var entry = Assert.Single(document.Skills);
        Assert.Equal(SafetySkillCatalog.SkillName, entry.Name);
        Assert.Equal("skill-md", entry.Type);
        Assert.Equal(SafetySkillCatalog.Description, entry.Description);
        Assert.Equal($"skill://{SafetySkillCatalog.SkillName}/SKILL.md", entry.Url);
    }

    [Fact]
    public void BuildSkillMarkdown_IncludesFrontMatterAndInstructions()
    {
        var markdown = SafetySkillCatalog.BuildSkillMarkdown(CreateTools().GetFunctions());

        Assert.Contains($"name: {SafetySkillCatalog.SkillName}", markdown);
        Assert.Contains($"description: {SafetySkillCatalog.Description}", markdown);
        Assert.Contains(SafetySkillCatalog.Instructions, markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ListsEveryToolAsAScript()
    {
        var tools = CreateTools().GetFunctions().ToList();

        var markdown = SafetySkillCatalog.BuildSkillMarkdown(tools);

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
