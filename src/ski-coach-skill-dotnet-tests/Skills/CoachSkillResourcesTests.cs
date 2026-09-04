using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SkiCoachSkill.Dotnet.Services;
using SkiCoachSkill.Dotnet.Skills;
using SkiCoachSkill.Dotnet.Tools;

namespace SkiCoachSkill.Dotnet.Tests.Skills;

public class CoachSkillResourcesTests
{
    private static CoachSkillResources CreateResources()
    {
        var tools = new CoachTools(new CoachDataService(new StubHttpClientFactory(), NullLogger<CoachDataService>.Instance));
        return new CoachSkillResources(tools);
    }

    [Fact]
    public void GetIndex_MatchesCatalogIndexJson()
    {
        var resources = CreateResources();

        Assert.Equal(CoachSkillCatalog.BuildIndexJson(), resources.GetIndex());
    }

    [Fact]
    public void GetSkillMd_MatchesCatalogMarkdownForTheSameTools()
    {
        var tools = new CoachTools(new CoachDataService(new StubHttpClientFactory(), NullLogger<CoachDataService>.Instance));
        var resources = new CoachSkillResources(tools);

        var expected = CoachSkillCatalog.BuildSkillMarkdown(tools.GetFunctions());

        Assert.Equal(expected, resources.GetSkillMd());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
