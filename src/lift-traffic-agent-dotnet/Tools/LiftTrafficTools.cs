using System.ComponentModel;
using Microsoft.Extensions.AI;
using LiftTrafficAgent.Dotnet.Services;

namespace LiftTrafficAgent.Dotnet.Tools;

public class LiftTrafficTools
{
    private readonly LiftDataService _liftDataService;

    public LiftTrafficTools(LiftDataService liftDataService)
    {
        _liftDataService = liftDataService;
    }

    [Description("List all ski lifts in the resort with their IDs, names, status, queue length, and wait times. Use this first to discover available lift IDs before querying a specific lift.")]
    public async Task<string> ListAllLifts()
    {
        return await _liftDataService.GetAllLiftsAsync();
    }

    [Description("Get the current status of a specific ski lift including wait time, queue length, and operational status")]
    public async Task<string> GetLiftStatus(
        [Description("The lift ID to check (e.g., 'chairlift-alpha', 'chairlift-bravo')")] string liftId)
    {
        return await _liftDataService.GetLiftByIdAsync(liftId);
    }

    [Description("Get current wait times for all ski lifts in the resort")]
    public async Task<string> GetWaitTimes()
    {
        return await _liftDataService.GetAllLiftsAsync();
    }

    [Description("Suggest the least congested area of the ski resort based on current lift wait times")]
    public async Task<string> SuggestLessBusyArea()
    {
        return await _liftDataService.SuggestLessBusyAreaAsync();
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        return
        [
            AIFunctionFactory.Create(ListAllLifts),
            AIFunctionFactory.Create(GetLiftStatus),
            AIFunctionFactory.Create(GetWaitTimes),
            AIFunctionFactory.Create(SuggestLessBusyArea)
        ];
    }
}
