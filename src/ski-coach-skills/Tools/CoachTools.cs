using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Server;
using SkiCoachSkill.Dotnet.Services;

namespace SkiCoachSkill.Dotnet.Tools;

[McpServerToolType]
public sealed class CoachTools(CoachDataService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };

    [McpServerTool(Name = "ski_coach_recommendations", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Recommend up to three open slopes using current resort state and the existing demo scoring rules.")]
    public async Task<SlopeRecommendations> Recommendations(
        [Description("Skier ability: beginner, intermediate, advanced, or expert."),
         AllowedValues("beginner", "intermediate", "advanced", "expert")] string skillLevel,
        CancellationToken cancellationToken,
        [Description("Optional comma-separated preferences: avoid_crowds, groomed_only. Unknown preferences are retained but ignored by scoring.")] string? preferences = null)
    {
        ValidateLevel(skillLevel);
        return Read<SlopeRecommendations>(await service.RecommendSlopeAsync(skillLevel, preferences, cancellationToken));
    }

    [McpServerTool(Name = "ski_coach_day_plan", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Build a morning, midday, and afternoon ski plan for the specified ability.")]
    public async Task<SkiDayPlan> DayPlan(
        [Description("Skier ability: beginner, intermediate, advanced, or expert."),
         AllowedValues("beginner", "intermediate", "advanced", "expert")] string skillLevel,
        CancellationToken cancellationToken)
    {
        ValidateLevel(skillLevel);
        return Read<SkiDayPlan>(await service.BuildDayPlanAsync(skillLevel, cancellationToken));
    }

    private static void ValidateLevel(string skillLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillLevel);
        if (!CoachDataService.SkillToDifficulty.ContainsKey(skillLevel.ToLowerInvariant()))
            throw new ArgumentOutOfRangeException(nameof(skillLevel), "Unknown skier ability.");
    }

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("Missing coach result.");
}

// Nullable weather/snow fields intentionally preserve the domain service's documented legacy field-name mismatch.
// MCP omits null properties, so these constructor parameters must also be optional in the output schema.
public sealed record CoachWeather(string? condition = null, double? temperature_c = null,
    double? wind_speed_kmh = null, double? visibility_km = null);
public sealed record RecommendationMetadata(string difficulty, string[] suitable_levels, int vertical_drop_m, int length_m, string[] features);
public sealed record SlopeConditions(bool is_open, bool groomed, string? snow_quality = null);
public sealed record SlopeRecommendation(string slope_id, string slope_name, string difficulty, double score,
    string[] reasons, RecommendationMetadata metadata, SlopeConditions current_conditions);
public sealed record SlopeRecommendations(string skill_level, Dictionary<string, bool> preferences,
    CoachWeather current_weather, SlopeRecommendation[] recommendations);
public sealed record PlanSlope(string name, string difficulty, string[] reasons);
public sealed record DayPlanSlot(string time_slot, string recommendation, PlanSlope[] slopes, string tips);
public sealed record SkiDayPlan(string skill_level, DayPlanSlot[] plan, CoachWeather weather_summary, string safety_notes);
