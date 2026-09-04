using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SkiResearcherSkill.Dotnet.Skills;

namespace SkiResearcherSkill.Dotnet.Tests.Skills;

public class SkiResearcherSkillCatalogTests
{
    // SkiResearcherTools requires a live Foundry-hosted AIAgent, which can't easily be stubbed/faked for a pure
    // unit test. Instead, this stand-in mirrors the exact same [Description] text via AIFunctionFactory.Create,
    // to validate BuildSkillMarkdown's rendering contract without needing a real agent.
    [Description("Searches the web for general skiing questions and returns a researched answer. Use for generic ski-related questions that are not resort-specific (e.g. ski technique, gear advice, ski history, or ski destinations elsewhere).")]
    private static Task<string> AskSkiResearcherAsync(string question) => Task.FromResult(string.Empty);

    private static IReadOnlyList<AIFunction> CreateFunctions() =>
    [
        AIFunctionFactory.Create(
            AskSkiResearcherAsync,
            name: "ask_ski_researcher",
            description: "Searches the web for general skiing questions and returns a researched answer. Use for generic ski-related questions that are not resort-specific (e.g. ski technique, gear advice, ski history, or ski destinations elsewhere).")
    ];

    [Fact]
    public void BuildIndexJson_DescribesTheSkiResearcherSkill()
    {
        var json = SkiResearcherSkillCatalog.BuildIndexJson();

        var document = JsonSerializer.Deserialize<SkiResearcherSkillCatalog.SkillIndexDocument>(json)
            ?? throw new InvalidOperationException("Failed to deserialize skill index document.");

        Assert.NotEmpty(document.Schema);
        var entry = Assert.Single(document.Skills);
        Assert.Equal(SkiResearcherSkillCatalog.SkillName, entry.Name);
        Assert.Equal("skill-md", entry.Type);
        Assert.Equal(SkiResearcherSkillCatalog.Description, entry.Description);
        Assert.Equal($"skill://{SkiResearcherSkillCatalog.SkillName}/SKILL.md", entry.Url);
    }

    [Fact]
    public void BuildSkillMarkdown_IncludesFrontMatterAndInstructions()
    {
        var markdown = SkiResearcherSkillCatalog.BuildSkillMarkdown(CreateFunctions());

        Assert.Contains($"name: {SkiResearcherSkillCatalog.SkillName}", markdown);
        Assert.Contains($"description: {SkiResearcherSkillCatalog.Description}", markdown);
        Assert.Contains(SkiResearcherSkillCatalog.Instructions, markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ListsTheAskSkiResearcherScript()
    {
        var functions = CreateFunctions();

        var markdown = SkiResearcherSkillCatalog.BuildSkillMarkdown(functions);

        foreach (var function in functions)
        {
            Assert.Contains($"`{function.Name}`", markdown);
            Assert.Contains(function.Description, markdown);
        }
    }
}
