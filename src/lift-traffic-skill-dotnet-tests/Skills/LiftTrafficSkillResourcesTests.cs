using System.Net.Http;
using LiftTrafficSkill.Dotnet.Services;
using LiftTrafficSkill.Dotnet.Skills;
using LiftTrafficSkill.Dotnet.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiftTrafficSkill.Dotnet.Tests.Skills;

public class LiftTrafficSkillResourcesTests
{
    private static LiftTrafficSkillResources CreateResources()
    {
        var tools = new LiftTrafficTools(new LiftDataService(new StubHttpClientFactory(), NullLogger<LiftDataService>.Instance));
        return new LiftTrafficSkillResources(tools);
    }

    [Fact]
    public void GetIndex_MatchesCatalogIndexJson()
    {
        var resources = CreateResources();

        Assert.Equal(LiftTrafficSkillCatalog.BuildIndexJson(), resources.GetIndex());
    }

    [Fact]
    public void GetSkillMd_MatchesCatalogMarkdownForTheSameTools()
    {
        var tools = new LiftTrafficTools(new LiftDataService(new StubHttpClientFactory(), NullLogger<LiftDataService>.Instance));
        var resources = new LiftTrafficSkillResources(tools);

        var expected = LiftTrafficSkillCatalog.BuildSkillMarkdown(tools.GetFunctions());

        Assert.Equal(expected, resources.GetSkillMd());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
