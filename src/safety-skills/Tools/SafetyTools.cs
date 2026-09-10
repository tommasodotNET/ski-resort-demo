using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using SafetySkill.Dotnet.Services;

namespace SafetySkill.Dotnet.Tools;

[McpServerToolType]
public sealed class SafetyTools(SafetyDataService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };

    [McpServerTool(Name = "safety_risk", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Evaluate resort risk and affected slopes. Area is a case-insensitive slope-name substring, not an enum.")]
    public async Task<RiskAssessment> Risk(
        CancellationToken cancellationToken,
        [Description("Slope-name substring, or 'all' (default) or empty string for the whole resort.")] string area = "all") =>
        Read<RiskAssessment>(await service.EvaluateRiskAsync(area, cancellationToken));

    [McpServerTool(Name = "safety_slope_safety", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Check safety for a slope ID. An unknown slope returns is_safe=false with a not-found reason.")]
    public async Task<SlopeSafety> SlopeSafety(
        [Description("Nonempty slope ID, for example valley-run."), MinLength(1)] string slopeId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slopeId);
        return Read<SlopeSafety>(await service.IsSlopeSafeAsync(slopeId, cancellationToken));
    }

    [McpServerTool(Name = "safety_closed_slopes", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("List slopes currently closed by resort management.")]
    public async Task<ClosedSlopes> ClosedSlopes(CancellationToken cancellationToken) =>
        Read<ClosedSlopes>(await service.GetClosedSlopesAsync(cancellationToken));

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("Missing safety result.");
}

public sealed record AffectedSlope(string slope_id, string name, string difficulty, bool is_open);
public sealed record RiskAssessment(string area, string risk_level, double risk_score, string[] factors,
    AffectedSlope[] affected_slopes, JsonObject weather, JsonObject[] incident_reports);
public sealed record SlopeSafety(string slope_id, bool is_safe, double risk_score, string[] reasons,
    string? slope_name = null, string? difficulty = null);
public sealed record ClosedSlope(string slope_id, string name, string difficulty, string[] reasons);
public sealed record ClosedSlopes(ClosedSlope[] closed_slopes, int total_closed);
