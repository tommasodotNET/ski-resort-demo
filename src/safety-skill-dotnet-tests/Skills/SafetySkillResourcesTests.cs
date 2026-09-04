using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SafetySkill.Dotnet.Services;
using SafetySkill.Dotnet.Skills;
using SafetySkill.Dotnet.Tools;

namespace SafetySkill.Dotnet.Tests.Skills;

public class SafetySkillResourcesTests
{
    private static SafetySkillResources CreateResources()
    {
        var tools = new SafetyTools(new SafetyDataService(new StubHttpClientFactory(), NullLogger<SafetyDataService>.Instance));
        return new SafetySkillResources(tools);
    }

    [Fact]
    public void GetIndex_MatchesCatalogIndexJson()
    {
        var resources = CreateResources();

        Assert.Equal(SafetySkillCatalog.BuildIndexJson(), resources.GetIndex());
    }

    [Fact]
    public void GetSkillMd_MatchesCatalogMarkdownForTheSameTools()
    {
        var tools = new SafetyTools(new SafetyDataService(new StubHttpClientFactory(), NullLogger<SafetyDataService>.Instance));
        var resources = new SafetySkillResources(tools);

        var expected = SafetySkillCatalog.BuildSkillMarkdown(tools.GetFunctions());

        Assert.Equal(expected, resources.GetSkillMd());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
