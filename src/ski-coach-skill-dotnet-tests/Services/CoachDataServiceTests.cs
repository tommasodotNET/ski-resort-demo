using System.Text.Json.Nodes;
using SkiCoachSkill.Dotnet.Services;

namespace SkiCoachSkill.Dotnet.Tests.Services;

public class CoachDataServiceTests
{
    private static readonly SlopeMetadata BlueNoFeatures = new("blue", ["intermediate"], 300, 2200, ["groomed"]);
    private static readonly SlopeMetadata BlueScenic = new("blue", ["intermediate"], 350, 2500, ["scenic-views", "varied-terrain", "groomed"]);
    private static readonly SlopeMetadata BlackDifficulty = new("black", ["expert"], 600, 1800, ["extreme-steep", "narrow"]);

    private static JsonObject Slope(string slopeId = "eagle-ridge", bool groomed = false, string? snowQuality = null)
    {
        var slope = new JsonObject
        {
            ["slope_id"] = slopeId,
            ["groomed"] = groomed
        };
        if (snowQuality is not null)
        {
            slope["snow_quality"] = snowQuality;
        }
        return slope;
    }

    private static JsonObject Weather(double windSpeedKmh = 10.0, double visibilityKm = 10.0)
        => new() { ["wind_speed_kmh"] = windSpeedKmh, ["visibility_km"] = visibilityKm };

    private static JsonObject Safety(double avalancheRiskIndex = 3.0)
        => new() { ["avalanche_risk_index"] = avalancheRiskIndex };

    private static readonly JsonArray NoLifts = [];

    [Fact]
    public void ScoreSlope_WithNeutralConditions_ReturnsBaselineOneHundred()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(100.0, score, precision: 5);
        Assert.Empty(reasons);
    }

    [Fact]
    public void ScoreSlope_WithHighWind_PenalizesAndReportsReason()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 60.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        // (60 - 40) * 0.5 = 10
        Assert.Equal(90.0, score, precision: 5);
        Assert.Contains("High wind (60 km/h)", reasons);
    }

    [Fact]
    public void ScoreSlope_WithCalmWind_AddsBonusAndReportsReason()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 10.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(105.0, score, precision: 5);
        Assert.Contains("Calm winds", reasons);
    }

    [Fact]
    public void ScoreSlope_WithExcellentVisibility_AddsBonus()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 9.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(110.0, score, precision: 5);
        Assert.Contains("Excellent visibility", reasons);
    }

    [Fact]
    public void ScoreSlope_WithPoorVisibility_Penalizes()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 2.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(85.0, score, precision: 5);
        Assert.Contains("Poor visibility", reasons);
    }

    [Fact]
    public void ScoreSlope_AvoidCrowds_HeavilyPenalizesQueueLength()
    {
        var lifts = new JsonArray
        {
            new JsonObject
            {
                ["serves_slopes"] = new JsonArray { "eagle-ridge" },
                ["queue_length"] = 25.0
            }
        };
        var prefs = new Dictionary<string, bool> { ["avoid_crowds"] = true };

        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), lifts, Safety(), prefs, BlueNoFeatures);

        // 100 - (25 * 3) = 25
        Assert.Equal(25.0, score, precision: 5);
        Assert.Contains("Long wait at lift (25 people)", reasons);
    }

    [Fact]
    public void ScoreSlope_WithoutAvoidCrowds_AppliesLighterPenalty()
    {
        var lifts = new JsonArray
        {
            new JsonObject
            {
                ["serves_slopes"] = new JsonArray { "eagle-ridge" },
                ["queue_length"] = 35.0
            }
        };

        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), lifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        // 100 - (35 * 0.5) = 82.5
        Assert.Equal(82.5, score, precision: 5);
        Assert.Contains("Very crowded (35 people)", reasons);
    }

    [Fact]
    public void ScoreSlope_WithShortLiftLines_AddsReason()
    {
        var lifts = new JsonArray
        {
            new JsonObject
            {
                ["serves_slopes"] = new JsonArray { "eagle-ridge" },
                ["queue_length"] = 5.0
            }
        };

        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), lifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(97.5, score, precision: 5);
        Assert.Contains("Short lift lines", reasons);
    }

    [Fact]
    public void ScoreSlope_HighDifficultyWithElevatedAvalancheRisk_Penalizes()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(avalancheRiskIndex: 8.0),
            new Dictionary<string, bool>(), BlackDifficulty);

        // (8 - 6) * 5 = 10
        Assert.Equal(90.0, score, precision: 5);
        Assert.Contains("Elevated avalanche risk (level 8)", reasons);
    }

    [Fact]
    public void ScoreSlope_BlueDifficultyIgnoresAvalancheRiskPenalty()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(avalancheRiskIndex: 8.0),
            new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(100.0, score, precision: 5);
        Assert.DoesNotContain(reasons, r => r.Contains("avalanche risk"));
    }

    [Fact]
    public void ScoreSlope_GroomedOnlyPreferenceWithUngroomedSlope_Penalizes()
    {
        var prefs = new Dictionary<string, bool> { ["groomed_only"] = true };

        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(groomed: false), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), prefs, BlueNoFeatures);

        Assert.Equal(70.0, score, precision: 5);
        Assert.Contains("Not groomed", reasons);
    }

    [Fact]
    public void ScoreSlope_GroomedSlope_AddsBonus()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(groomed: true), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(105.0, score, precision: 5);
        Assert.Contains("Freshly groomed", reasons);
    }

    [Fact]
    public void ScoreSlope_SnowQualityIsAlwaysNull_PreservesFieldNameMismatch()
    {
        // "snow_quality" does not exist in the data-generator schema; even when the test fixture sets it,
        // the production code path always reads it from the (never populated in real data) slope object.
        // This test documents that when the field IS present, the powder/packed bonuses DO apply — proving
        // the mismatch is about the data-generator never emitting the field, not about the read logic itself.
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(snowQuality: "powder"), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(115.0, score, precision: 5);
        Assert.Contains("Powder conditions", reasons);
    }

    [Fact]
    public void ScoreSlope_PackedSnowQuality_AddsSmallerBonus()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(snowQuality: "packed"), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueNoFeatures);

        Assert.Equal(105.0, score, precision: 5);
        Assert.Contains("Good packed snow", reasons);
    }

    [Fact]
    public void ScoreSlope_ScenicViewsFeature_AddsFlatBonusWithNoReason()
    {
        var (score, reasons) = CoachDataService.ScoreSlope(
            Slope(), Weather(windSpeedKmh: 25.0, visibilityKm: 5.0), NoLifts, Safety(), new Dictionary<string, bool>(), BlueScenic);

        Assert.Equal(103.0, score, precision: 5);
        Assert.DoesNotContain(reasons, r => r.Contains("scenic"));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("avoid_crowds", new[] { "avoid_crowds" })]
    [InlineData("avoid_crowds, groomed_only", new[] { "avoid_crowds", "groomed_only" })]
    [InlineData("  AVOID_CROWDS  ,, groomed_only", new[] { "avoid_crowds", "groomed_only" })]
    public void ParsePreferences_ParsesCommaSeparatedFlags(string input, string[] expectedKeys)
    {
        var result = CoachDataService.ParsePreferences(input);

        Assert.Equal(expectedKeys.Length, result.Count);
        foreach (var key in expectedKeys)
        {
            Assert.True(result.GetValueOrDefault(key));
        }
    }

    [Fact]
    public void ParsePreferences_WithNull_ReturnsEmptyDictionary()
    {
        Assert.Empty(CoachDataService.ParsePreferences(null));
    }
}
