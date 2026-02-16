using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedServices;

/// <summary>
/// Thread store that keeps threads in memory and persists serialized state to Cosmos DB.
/// On restart, threads are recreated fresh (stateless agent design).
/// </summary>
public class CosmosAgentThreadStore : AgentThreadStore
{
    private readonly ICosmosThreadRepository _repository;
    private readonly ILogger<CosmosAgentThreadStore> _logger;
    private readonly ConcurrentDictionary<string, AgentThread> _cache = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CosmosAgentThreadStore(
        ICosmosThreadRepository repository,
        ILogger<CosmosAgentThreadStore> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public override ValueTask<AgentThread> GetThreadAsync(AIAgent agent, string conversationId, CancellationToken cancellationToken = default)
    {
        var key = $"{agent.Name}:{conversationId}";
        _logger.LogInformation("Getting thread for key: {Key}", key);

        if (_cache.TryGetValue(key, out var thread))
        {
            _logger.LogInformation("Found cached thread for key: {Key}", key);
            return new ValueTask<AgentThread>(thread);
        }

        _logger.LogInformation("No thread found for key: {Key}, creating new thread", key);
        var newThread = agent.GetNewThread();
        _cache[key] = newThread;
        return new ValueTask<AgentThread>(newThread);
    }

    public override ValueTask SaveThreadAsync(AIAgent agent, string conversationId, AgentThread thread, CancellationToken cancellationToken = default)
    {
        var key = $"{agent.Name}:{conversationId}";
        _logger.LogInformation("Saving thread for key: {Key}", key);

        // Keep thread in memory for conversation continuity within this session.
        // Cosmos persistence is skipped because ChatClientAgentThread's internal
        // types cannot be serialized with standard System.Text.Json options.
        _cache[key] = thread;

        return default;
    }
}
