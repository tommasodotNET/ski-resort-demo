using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Server;
using LiftTrafficSkill.Dotnet.Services;

namespace LiftTrafficSkill.Dotnet.Tools;

[McpServerToolType]
public sealed class LiftTrafficTools(LiftDataService service)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };

    [McpServerTool(Name = "lift_traffic_lifts", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Read all lift statuses, queues, wait times, and served slopes.")]
    public async Task<LiftList> Lifts(CancellationToken cancellationToken) =>
        new(Read<LiftStatus[]>(await service.GetAllLiftsAsync(cancellationToken)));

    [McpServerTool(Name = "lift_traffic_lift_status", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Read a single lift by ID. An unknown ID is an upstream error.")]
    public async Task<LiftStatus> LiftStatus(
        [Description("Nonempty lift ID, for example lift-1."), MinLength(1)] string liftId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liftId);
        return Read<LiftStatus>(await service.GetLiftByIdAsync(liftId, cancellationToken));
    }

    [McpServerTool(Name = "lift_traffic_wait_times", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Read wait times with full lift status context; same live data as the all-lifts operation.")]
    public Task<LiftList> WaitTimes(CancellationToken cancellationToken) => Lifts(cancellationToken);

    [McpServerTool(Name = "lift_traffic_least_busy_area", ReadOnly = true, Destructive = false, UseStructuredContent = true)]
    [Description("Recommend the open lift with the shortest wait, or report that no lifts are open.")]
    public async Task<LeastBusyArea> LeastBusyArea(CancellationToken cancellationToken) =>
        Read<LeastBusyArea>(await service.SuggestLessBusyAreaAsync(cancellationToken));

    private static T Read<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("Missing lift result.");
}

public sealed record LiftStatus(string lift_id, string name, string status, int queue_length, double wait_time_minutes,
    int throughput_rate, string[] serves_slopes, string timestamp);
public sealed record LiftList(LiftStatus[] lifts);
public sealed record LeastBusyArea(string recommendation, string? liftId = null, string? liftName = null,
    double? waitTimeMinutes = null, double? waitTime = null);
