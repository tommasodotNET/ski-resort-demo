using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedServices;

public sealed class CosmosAgentSessionStore : AgentSessionStore
{
    private readonly ICosmosThreadRepository _repository;
    private readonly JsonSerializerOptions _options;

    private readonly ILogger<CosmosAgentSessionStore> _logger;

    public CosmosAgentSessionStore(
        ICosmosThreadRepository repository,
        ILogger<CosmosAgentSessionStore> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
    }

    public override async ValueTask SaveSessionAsync(
        AIAgent agent,
        string conversationId,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(conversationId, agent.Name);
        var serializedThread = await agent.SerializeSessionAsync(session, _options, cancellationToken);
        
        _logger.LogInformation("Saving thread for conversation {ConversationId} and agent {AgentName}", conversationId, agent.Name);
        await _repository.SaveThreadAsync(key, serializedThread, cancellationToken);
    }

    public override async ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(conversationId, agent.Name);
        var serializedThread = await _repository.GetThreadAsync(key, cancellationToken);

        if (serializedThread == null)
        {
            _logger.LogInformation("Creating new session for conversation {ConversationId} and agent {AgentName}", conversationId, agent.Name);
            return await agent.CreateSessionAsync(cancellationToken);
        }

        _logger.LogInformation("Loading existing session for conversation {ConversationId} and agent {AgentName}", conversationId, agent.Name);
        return await agent.DeserializeSessionAsync(serializedThread.Value, _options, cancellationToken);
    }

    private static string GetKey(string conversationId, string agentName) => $"{agentName}:{conversationId}";
}
