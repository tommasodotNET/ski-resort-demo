using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using WeatherSkill.Dotnet.Services;
using WeatherSkill.Dotnet.Skills;
using WeatherSkill.Dotnet.Tools;

namespace WeatherSkill.Dotnet.Tests.Skills;

public class WeatherSkillResourcesTests
{
    private static WeatherSkillResources CreateResources()
    {
        var tools = new WeatherTools(new WeatherDataService(new StubHttpClientFactory(), NullLogger<WeatherDataService>.Instance));
        return new WeatherSkillResources(tools);
    }

    [Fact]
    public void GetIndex_MatchesCatalogIndexJson()
    {
        var resources = CreateResources();

        Assert.Equal(WeatherSkillCatalog.BuildIndexJson(), resources.GetIndex());
    }

    [Fact]
    public void GetSkillMd_MatchesCatalogMarkdownForTheSameTools()
    {
        var tools = new WeatherTools(new WeatherDataService(new StubHttpClientFactory(), NullLogger<WeatherDataService>.Instance));
        var resources = new WeatherSkillResources(tools);

        var expected = WeatherSkillCatalog.BuildSkillMarkdown(tools.GetFunctions());

        Assert.Equal(expected, resources.GetSkillMd());
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
