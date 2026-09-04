using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Agents.AI.Foundry.Hosting;

/// <summary>
/// A local-development isolation key provider for hosted Foundry agent sessions.
/// </summary>
public sealed class DevTemporaryLocalUserIdProvider : HostedSessionIsolationKeyProvider
{
    public const string UserIdEnvironmentVariable = "HOSTED_USER_ID";
    public const string DefaultLocalUserId = "local-dev-user";

    public override ValueTask<HostedSessionContext?> GetKeysAsync(
        ResponseContext context,
        CreateResponse request,
        CancellationToken cancellationToken)
    {
        var userId = !string.IsNullOrWhiteSpace(context?.PlatformContext?.UserIdKey)
            ? context!.PlatformContext!.UserIdKey
            : Environment.GetEnvironmentVariable(UserIdEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = DefaultLocalUserId;
        }

        return new ValueTask<HostedSessionContext?>(new HostedSessionContext(userId));
    }
}
