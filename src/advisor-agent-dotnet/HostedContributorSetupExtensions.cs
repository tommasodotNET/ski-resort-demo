using Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// Registration helpers for developer-only local hosted-agent contributor utilities.
/// </summary>
public static class HostedContributorSetupExtensions
{
    /// <summary>
    /// Registers services that let a hosted Foundry agent run outside the Foundry platform during local debugging.
    /// </summary>
    public static IServiceCollection AddDevTemporaryLocalContributorSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<HostedSessionIsolationKeyProvider, DevTemporaryLocalUserIdProvider>();

        return services;
    }
}
