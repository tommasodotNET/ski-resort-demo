using System.Text.Json.Nodes;
using SafetySkill.Dotnet.Services;

namespace SafetySkill.Dotnet.Tests.Services;

public class SafetyDataServiceTests
{
    [Fact]
    public void CalculateRiskScore_WithBenignConditions_ReturnsLowScoreAndNoFactors()
    {
        var weather = new JsonObject
        {
            ["temperature"] = -5.0,
            ["wind_speed"] = 10.0,
            ["snow_intensity"] = 1.0,
            ["visibility"] = 8000.0
        };
        var safety = new JsonObject
        {
            ["avalanche_risk_index"] = 0.0,
            ["incident_reports"] = new JsonArray()
        };

        var (riskScore, factors) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(0.0, riskScore);
        Assert.Empty(factors);
    }

    [Theory]
    [InlineData(60, 0.2, "Extreme wind speed: 60 km/h")]
    [InlineData(35, 0.1, "High wind speed: 35 km/h")]
    public void CalculateRiskScore_AppliesWindSpeedThresholds(double windSpeed, double expectedContribution, string expectedFactor)
    {
        var weather = new JsonObject { ["wind_speed"] = windSpeed, ["visibility"] = 5000.0, ["snow_intensity"] = 0.0 };
        var safety = new JsonObject { ["avalanche_risk_index"] = 0.0 };

        var (riskScore, factors) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(expectedContribution, riskScore, precision: 5);
        Assert.Contains(expectedFactor, factors);
    }

    [Theory]
    [InlineData(400, 0.15, "Very low visibility: 400m")]
    [InlineData(800, 0.05, "Low visibility: 800m")]
    public void CalculateRiskScore_AppliesVisibilityThresholds(double visibility, double expectedContribution, string expectedFactor)
    {
        var weather = new JsonObject { ["wind_speed"] = 0.0, ["visibility"] = visibility, ["snow_intensity"] = 0.0 };
        var safety = new JsonObject { ["avalanche_risk_index"] = 0.0 };

        var (riskScore, factors) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(expectedContribution, riskScore, precision: 5);
        Assert.Contains(expectedFactor, factors);
    }

    [Fact]
    public void CalculateRiskScore_AppliesHeavySnowfallThreshold()
    {
        var weather = new JsonObject { ["wind_speed"] = 0.0, ["visibility"] = 5000.0, ["snow_intensity"] = 4.0 };
        var safety = new JsonObject { ["avalanche_risk_index"] = 0.0 };

        var (riskScore, factors) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(0.1, riskScore, precision: 5);
        Assert.Contains("Heavy snowfall: intensity 4", factors);
    }

    [Fact]
    public void CalculateRiskScore_IncludesAvalancheRiskIndexAsFactorWhenPositive()
    {
        var weather = new JsonObject { ["wind_speed"] = 0.0, ["visibility"] = 5000.0, ["snow_intensity"] = 0.0 };
        var safety = new JsonObject { ["avalanche_risk_index"] = 0.4 };

        var (riskScore, factors) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(0.4, riskScore, precision: 5);
        Assert.Contains("Avalanche risk index: 0.40", factors);
    }

    [Fact]
    public void CalculateRiskScore_ClampsToOneWhenContributionsExceedIt()
    {
        var weather = new JsonObject { ["wind_speed"] = 100.0, ["visibility"] = 100.0, ["snow_intensity"] = 10.0 };
        var safety = new JsonObject { ["avalanche_risk_index"] = 0.9 };

        var (riskScore, _) = SafetyDataService.CalculateRiskScore(weather, safety);

        Assert.Equal(1.0, riskScore);
    }

    [Theory]
    [InlineData(0.0, "low")]
    [InlineData(0.29, "low")]
    [InlineData(0.3, "moderate")]
    [InlineData(0.49, "moderate")]
    [InlineData(0.5, "high")]
    [InlineData(0.69, "high")]
    [InlineData(0.7, "critical")]
    [InlineData(1.0, "critical")]
    public void GetRiskLevel_MapsScoreToExpectedLevel(double riskScore, string expectedLevel)
    {
        Assert.Equal(expectedLevel, SafetyDataService.GetRiskLevel(riskScore));
    }
}
