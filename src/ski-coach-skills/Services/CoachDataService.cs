using System.Text.Json;
using System.Text.Json.Nodes;

namespace SkiCoachSkill.Dotnet.Services;

/// <summary>Static metadata for a named slope. Mirrors <c>CoachService.SLOPE_METADATA</c> entries.</summary>
public sealed record SlopeMetadata(
    string Difficulty,
    IReadOnlyList<string> SuitableLevels,
    int VerticalDropM,
    int LengthM,
    IReadOnlyList<string> Features);

/// <summary>
/// Fetches resort state from the data-generator service and applies the slope-recommendation / day-planning
/// scoring engine.
/// </summary>
/// <remarks>
/// This is a faithful .NET port of <c>ski-coach-agent-a2a</c>'s <c>CoachService</c>
/// (<c>services/coach_service.py</c>): same <c>SLOPE_METADATA</c> table, same skill-to-difficulty mapping, same
/// <see cref="ScoreSlope"/> rule engine, and the same <c>recommend_slope</c> / <c>build_day_plan</c> algorithms.
/// The existing Python A2A agent is left untouched; this service backs a new, additive MCP skill-provider server.
/// </remarks>
/// <remarks>
/// <b>Known pre-existing field-name mismatch, preserved as-is:</b> <c>coach_service.py</c>'s scoring/output logic
/// reads weather fields as <c>wind_speed_kmh</c>, <c>visibility_km</c>, <c>condition</c>, and <c>temperature_c</c>,
/// and slope fields as <c>slope_name</c> and <c>snow_quality</c>. The data-generator's actual JSON schema
/// (<c>src/data-generator/main.go</c>) uses <c>wind_speed</c>, <c>visibility</c>, <c>temperature</c> (no
/// <c>condition</c> field), and <c>name</c> (no <c>slope_name</c>/<c>snow_quality</c>). In the existing Python
/// agent this means those specific lookups silently fall back to their <c>.get(key, default)</c> defaults (or
/// <c>None</c> where no default is given) rather than reflecting live data — this is existing, observable
/// behavior of <c>ski-coach-agent-a2a</c> today, not something introduced by this port. To faithfully
/// preserve current behavior, this port intentionally reads the same (mismatched) field names rather than
/// "fixing" them to <c>wind_speed</c>/<c>visibility</c>/<c>name</c>.
/// </remarks>
public class CoachDataService
{
    private const string DataGeneratorUrl = "https+http://datagenerator";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Metadata for all slopes in the resort. Mirrors <c>CoachService.SLOPE_METADATA</c> exactly.</summary>
    public static readonly IReadOnlyDictionary<string, SlopeMetadata> SlopeMetadataTable =
        new Dictionary<string, SlopeMetadata>
        {
            ["valley-run"] = new("green", ["beginner"], 150, 1200, ["wide", "gentle", "perfect-for-learning"]),
            ["sunrise-trail"] = new("green", ["beginner"], 180, 1400, ["scenic", "wide", "groomed"]),
            ["alpine-meadow"] = new("blue", ["intermediate", "beginner"], 300, 2200, ["cruising", "groomed", "family-friendly"]),
            ["eagle-ridge"] = new("blue", ["intermediate"], 350, 2500, ["scenic-views", "varied-terrain", "groomed"]),
            ["timber-bowl"] = new("blue", ["intermediate", "advanced"], 400, 2800, ["tree-skiing", "powder-stashes", "challenging"]),
            ["north-face"] = new("red", ["advanced"], 500, 2400, ["steep", "moguls", "expert-territory"]),
            ["summit-chute"] = new("black", ["expert"], 600, 1800, ["extreme-steep", "narrow", "experts-only"]),
            ["avalanche-alley"] = new("black", ["expert"], 650, 2000, ["off-piste", "challenging", "backcountry-style"]),
        };

    /// <summary>Maps skill levels to suitable difficulties. Mirrors <c>CoachService.SKILL_TO_DIFFICULTY</c> exactly.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SkillToDifficulty =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["beginner"] = ["green", "blue"],
            ["intermediate"] = ["blue", "red"],
            ["advanced"] = ["red", "black"],
            ["expert"] = ["black", "red"],
        };

    private static readonly SlopeMetadata FallbackMetadata = new("blue", [], 0, 0, []);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoachDataService> _logger;

    public CoachDataService(IHttpClientFactory httpClientFactory, ILogger<CoachDataService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _logger.LogInformation("CoachDataService initialized with data-generator URL: {Url}", DataGeneratorUrl);
    }

    private HttpClient CreateDataGeneratorClient()
    {
        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(DataGeneratorUrl);
        return httpClient;
    }

    /// <summary>
    /// Fetches the combined resort state (weather, lifts, safety, slopes). Mirrors
    /// <c>CoachService._fetch_current_state</c>: on failure, throws instead of returning fallback data
    /// (the Python service does not catch this internally either — it re-raises).
    /// </summary>
    private async Task<JsonObject> FetchCurrentStateAsync()
    {
        try
        {
            var httpClient = CreateDataGeneratorClient();
            var response = await httpClient.GetAsync("/api/current-state");
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching resort state");
            throw new InvalidOperationException($"Failed to fetch resort state: {ex.Message}", ex);
        }
    }

    /// <summary>Parses a comma-separated preferences string into a set of enabled preference flags.</summary>
    public static Dictionary<string, bool> ParsePreferences(string? preferences)
    {
        var result = new Dictionary<string, bool>();
        if (string.IsNullOrWhiteSpace(preferences))
        {
            return result;
        }

        foreach (var raw in preferences.ToLowerInvariant().Split(','))
        {
            var pref = raw.Trim();
            if (pref.Length > 0)
            {
                result[pref] = true;
            }
        }

        return result;
    }

    /// <summary>Finds the lift that serves the given slope. Mirrors <c>CoachService._find_slope_lift</c>.</summary>
    private static JsonObject? FindSlopeLift(string slopeId, JsonArray lifts)
    {
        foreach (var lift in lifts)
        {
            var servesSlopes = lift?["serves_slopes"] as JsonArray;
            if (servesSlopes is null)
            {
                continue;
            }

            foreach (var served in servesSlopes)
            {
                if (served?.GetValue<string>() == slopeId)
                {
                    return lift as JsonObject;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Scores a slope based on current conditions and preferences. Pure rule engine — no I/O — mirrors
    /// <c>CoachService._score_slope</c> exactly, including its known field-name mismatches (see class remarks).
    /// </summary>
    public static (double Score, List<string> Reasons) ScoreSlope(
        JsonObject slope,
        JsonObject weather,
        JsonArray lifts,
        JsonObject safety,
        IReadOnlyDictionary<string, bool> preferences,
        SlopeMetadata metadata)
    {
        var score = 100.0;
        var reasons = new List<string>();

        // NOTE: "wind_speed_kmh" / "visibility_km" do not exist in the data-generator's actual weather schema
        // (which uses "wind_speed" / "visibility"); this mismatch is pre-existing in coach_service.py and is
        // preserved here rather than silently "fixed" (see class remarks).
        var windSpeed = weather["wind_speed_kmh"]?.GetValue<double>() ?? 0;
        var visibility = weather["visibility_km"]?.GetValue<double>() ?? 10;

        if (windSpeed > 40)
        {
            var penalty = (windSpeed - 40) * 0.5;
            score -= penalty;
            reasons.Add($"High wind ({windSpeed} km/h)");
        }
        else if (windSpeed < 20)
        {
            score += 5;
            reasons.Add("Calm winds");
        }

        if (visibility > 8)
        {
            score += 10;
            reasons.Add("Excellent visibility");
        }
        else if (visibility < 3)
        {
            score -= 15;
            reasons.Add("Poor visibility");
        }

        var slopeId = slope["slope_id"]?.GetValue<string>() ?? string.Empty;
        var lift = FindSlopeLift(slopeId, lifts);
        if (lift is not null)
        {
            var queueLength = lift["queue_length"]?.GetValue<double>() ?? 0;
            if (preferences.GetValueOrDefault("avoid_crowds"))
            {
                var penalty = queueLength * 3; // Heavy penalty for crowds if preferred
                score -= penalty;
                if (queueLength > 20)
                {
                    reasons.Add($"Long wait at lift ({queueLength} people)");
                }
            }
            else
            {
                var penalty = queueLength * 0.5;
                score -= penalty;
                if (queueLength > 30)
                {
                    reasons.Add($"Very crowded ({queueLength} people)");
                }
            }

            if (queueLength < 10)
            {
                reasons.Add("Short lift lines");
            }
        }

        // NOTE: the data-generator's actual "avalanche_risk_index" is a 0-1 float (see safety-skills /
        // safety-agent-a2a), while this comparison assumes a 0-10 scale — another pre-existing mismatch in
        // coach_service.py, preserved here as-is (see class remarks).
        var avalancheRisk = safety["avalanche_risk_index"]?.GetValue<double>() ?? 3;
        var difficulty = metadata.Difficulty;

        if ((difficulty == "black" || difficulty == "red") && avalancheRisk > 6)
        {
            var penalty = (avalancheRisk - 6) * 5;
            score -= penalty;
            reasons.Add($"Elevated avalanche risk (level {avalancheRisk})");
        }

        // NOTE: "snow_quality" does not exist in the data-generator's actual slope schema (which uses
        // "snow_depth_cm" instead) — always null/None here; pre-existing mismatch preserved (see class remarks).
        var groomed = slope["groomed"]?.GetValue<bool>() ?? false;
        if (preferences.GetValueOrDefault("groomed_only") && !groomed)
        {
            score -= 30;
            reasons.Add("Not groomed");
        }
        else if (groomed)
        {
            score += 5;
            reasons.Add("Freshly groomed");
        }

        var snowQuality = slope["snow_quality"]?.GetValue<string>();
        if (snowQuality == "powder")
        {
            score += 15;
            reasons.Add("Powder conditions");
        }
        else if (snowQuality == "packed")
        {
            score += 5;
            reasons.Add("Good packed snow");
        }

        if (metadata.Features.Contains("scenic-views") || metadata.Features.Contains("scenic"))
        {
            score += 3;
        }

        return (score, reasons);
    }

    /// <summary>
    /// Recommends up to 3 slopes based on skill level and preferences. Mirrors
    /// <c>CoachService.recommend_slope</c> exactly.
    /// </summary>
    public async Task<string> RecommendSlopeAsync(string skillLevel, string? preferences)
    {
        skillLevel = skillLevel.ToLowerInvariant();
        if (!SkillToDifficulty.TryGetValue(skillLevel, out var suitableDifficulties))
        {
            throw new ArgumentException(
                $"Invalid skill level: {skillLevel}. Must be one of: beginner, intermediate, advanced, expert");
        }

        var prefs = ParsePreferences(preferences);

        var state = await FetchCurrentStateAsync();
        var slopes = state["slopes"] as JsonArray ?? new JsonArray();
        var weather = state["weather"] as JsonObject ?? new JsonObject();
        var lifts = state["lifts"] as JsonArray ?? new JsonArray();
        var safety = state["safety"] as JsonObject ?? new JsonObject();

        var candidates = new List<(string SlopeId, string SlopeName, string Difficulty, double Score, List<string> Reasons, SlopeMetadata Metadata, JsonObject Slope)>();

        foreach (var slopeNode in slopes)
        {
            if (slopeNode is not JsonObject slope)
            {
                continue;
            }

            var slopeId = slope["slope_id"]?.GetValue<string>() ?? string.Empty;
            var metadata = SlopeMetadataTable.GetValueOrDefault(slopeId, FallbackMetadata);

            if (!(slope["is_open"]?.GetValue<bool>() ?? false))
            {
                continue;
            }

            var difficulty = metadata.Difficulty;
            if (!suitableDifficulties.Contains(difficulty))
            {
                continue;
            }

            if (prefs.GetValueOrDefault("groomed_only") && !(slope["groomed"]?.GetValue<bool>() ?? false))
            {
                continue;
            }

            var (score, reasons) = ScoreSlope(slope, weather, lifts, safety, prefs, metadata);

            // NOTE: "slope_name" does not exist in the data-generator's actual slope schema (which uses "name")
            // — this always falls back to slope_id; pre-existing mismatch preserved (see class remarks).
            var slopeName = slope["slope_name"]?.GetValue<string>() ?? slopeId;

            candidates.Add((slopeId, slopeName, difficulty, score, reasons, metadata, slope));
        }

        var top = candidates.OrderByDescending(c => c.Score).Take(3).ToList();

        var recommendations = new JsonArray();
        foreach (var c in top)
        {
            recommendations.Add(new JsonObject
            {
                ["slope_id"] = c.SlopeId,
                ["slope_name"] = c.SlopeName,
                ["difficulty"] = c.Difficulty,
                ["score"] = c.Score,
                ["reasons"] = new JsonArray(c.Reasons.Select(r => (JsonNode)r).ToArray()),
                ["metadata"] = MetadataToJson(c.Metadata),
                ["current_conditions"] = new JsonObject
                {
                    ["is_open"] = c.Slope["is_open"]?.DeepClone(),
                    ["groomed"] = c.Slope["groomed"]?.DeepClone(),
                    ["snow_quality"] = c.Slope["snow_quality"]?.DeepClone()
                }
            });
        }

        var result = new JsonObject
        {
            ["skill_level"] = skillLevel,
            ["preferences"] = new JsonObject(prefs.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value))),
            ["current_weather"] = new JsonObject
            {
                ["condition"] = weather["condition"]?.DeepClone(),
                ["temperature_c"] = weather["temperature_c"]?.DeepClone(),
                ["wind_speed_kmh"] = weather["wind_speed_kmh"]?.DeepClone(),
                ["visibility_km"] = weather["visibility_km"]?.DeepClone()
            },
            ["recommendations"] = recommendations
        };

        return result.ToJsonString(SerializerOptions);
    }

    /// <summary>Builds a full day ski plan based on skill level. Mirrors <c>CoachService.build_day_plan</c> exactly.</summary>
    public async Task<string> BuildDayPlanAsync(string skillLevel)
    {
        skillLevel = skillLevel.ToLowerInvariant();
        if (!SkillToDifficulty.TryGetValue(skillLevel, out var suitableDifficulties))
        {
            throw new ArgumentException(
                $"Invalid skill level: {skillLevel}. Must be one of: beginner, intermediate, advanced, expert");
        }

        var state = await FetchCurrentStateAsync();
        var slopes = state["slopes"] as JsonArray ?? new JsonArray();
        var weather = state["weather"] as JsonObject ?? new JsonObject();
        var lifts = state["lifts"] as JsonArray ?? new JsonArray();
        var safety = state["safety"] as JsonObject ?? new JsonObject();

        var slopeData = new List<(string SlopeName, string Difficulty, double Score, List<string> Reasons)>();

        foreach (var slopeNode in slopes)
        {
            if (slopeNode is not JsonObject slope)
            {
                continue;
            }

            if (!(slope["is_open"]?.GetValue<bool>() ?? false))
            {
                continue;
            }

            var slopeId = slope["slope_id"]?.GetValue<string>() ?? string.Empty;
            var metadata = SlopeMetadataTable.GetValueOrDefault(slopeId, FallbackMetadata);
            var difficulty = metadata.Difficulty;

            if (!suitableDifficulties.Contains(difficulty))
            {
                continue;
            }

            var (score, reasons) = ScoreSlope(slope, weather, lifts, safety, new Dictionary<string, bool>(), metadata);
            var slopeName = slope["slope_name"]?.GetValue<string>() ?? slopeId;

            slopeData.Add((slopeName, difficulty, score, reasons));
        }

        slopeData = slopeData.OrderByDescending(s => s.Score).ToList();

        var plan = new JsonArray();

        // Morning: warm-up on easier slopes
        var morningSlopes = slopeData.Where(s => s.Difficulty is "green" or "blue").Take(2).ToList();
        if (morningSlopes.Count == 0)
        {
            morningSlopes = slopeData.Take(2).ToList();
        }

        plan.Add(new JsonObject
        {
            ["time_slot"] = "Morning (9:00 - 12:00)",
            ["recommendation"] = "Warm-up session - Start with easier slopes to get your legs ready",
            ["slopes"] = BuildSlopeSlotEntries(morningSlopes, 2),
            ["tips"] = "Take it easy and focus on technique. Check your equipment and get comfortable."
        });

        // Midday: break and less crowded slopes
        var middaySlopes = slopeData
            .Where(s => s.Reasons.Any(r => r.Contains("Short lift lines") || r.ToLowerInvariant().Contains("calm")))
            .Take(2)
            .ToList();
        if (middaySlopes.Count == 0)
        {
            middaySlopes = slopeData.Count > 2 ? slopeData.Skip(2).Take(2).ToList() : slopeData.Take(2).ToList();
        }

        plan.Add(new JsonObject
        {
            ["time_slot"] = "Midday (12:00 - 14:00)",
            ["recommendation"] = "Lunch break and light skiing - Avoid peak crowds",
            ["slopes"] = BuildSlopeSlotEntries(middaySlopes, 2),
            ["tips"] = "Stay hydrated and take a proper lunch break. Ski a few lighter runs to stay loose."
        });

        // Afternoon: best conditions
        var afternoonSlopes = slopeData.Take(3).ToList();

        plan.Add(new JsonObject
        {
            ["time_slot"] = "Afternoon (14:00 - 16:00)",
            ["recommendation"] = "Prime time - Best conditions and your peak performance",
            ["slopes"] = BuildSlopeSlotEntries(afternoonSlopes, 3),
            ["tips"] = "You're warmed up and conditions are optimal. Push yourself but know your limits!"
        });

        var avalancheRiskForNotes = safety["avalanche_risk_index"]?.GetValue<double>() ?? 3;

        var result = new JsonObject
        {
            ["skill_level"] = skillLevel,
            ["plan"] = plan,
            ["weather_summary"] = new JsonObject
            {
                ["condition"] = weather["condition"]?.DeepClone(),
                ["temperature_c"] = weather["temperature_c"]?.DeepClone(),
                ["wind_speed_kmh"] = weather["wind_speed_kmh"]?.DeepClone()
            },
            ["safety_notes"] =
                $"Avalanche risk: {avalancheRiskForNotes}/10. Always ski within your ability and follow resort safety guidelines."
        };

        return result.ToJsonString(SerializerOptions);
    }

    private static JsonArray BuildSlopeSlotEntries(
        IEnumerable<(string SlopeName, string Difficulty, double Score, List<string> Reasons)> slopes,
        int reasonCount)
    {
        var array = new JsonArray();
        foreach (var s in slopes)
        {
            array.Add(new JsonObject
            {
                ["name"] = s.SlopeName,
                ["difficulty"] = s.Difficulty,
                ["reasons"] = new JsonArray(s.Reasons.Take(reasonCount).Select(r => (JsonNode)r).ToArray())
            });
        }

        return array;
    }

    private static JsonObject MetadataToJson(SlopeMetadata metadata) => new()
    {
        ["difficulty"] = metadata.Difficulty,
        ["suitable_levels"] = new JsonArray(metadata.SuitableLevels.Select(s => (JsonNode)s).ToArray()),
        ["vertical_drop_m"] = metadata.VerticalDropM,
        ["length_m"] = metadata.LengthM,
        ["features"] = new JsonArray(metadata.Features.Select(f => (JsonNode)f).ToArray())
    };
}
