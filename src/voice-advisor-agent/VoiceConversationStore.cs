using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace VoiceAdvisorAgent;

/// <summary>
/// Handles loading and saving voice conversation history to Cosmos DB.
/// </summary>
public sealed class VoiceConversationStore
{
    private readonly Container _container;
    private readonly ILogger _logger;

    public VoiceConversationStore(Container container, ILogger logger)
    {
        _container = container;
        _logger = logger;
    }

    /// <summary>
    /// Loads previous conversation messages from Cosmos DB for the given conversation ID.
    /// Returns a list of (role, text) tuples ordered by timestamp.
    /// </summary>
    public async Task<List<(string role, string text)>> LoadAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.conversationId = @convId AND c.type = @type ORDER BY c.timestamp ASC")
            .WithParameter("@convId", conversationId)
            .WithParameter("@type", "ChatMessage");

        var messages = new List<(string role, string text)>();
        using var iterator = _container.GetItemQueryIterator<JsonElement>(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(conversationId) });

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            foreach (var doc in response)
            {
                var role = doc.GetProperty("role").GetString() ?? "";
                var text = doc.GetProperty("message").GetString() ?? "";
                if (!string.IsNullOrEmpty(text))
                    messages.Add((role, text));
            }
        }

        if (messages.Count > 0)
            _logger.LogInformation("Loaded {Count} previous conversation messages for {ConversationId}",
                messages.Count, conversationId);

        return messages;
    }

    /// <summary>
    /// Saves conversation messages to Cosmos DB after a voice session ends.
    /// </summary>
    public async Task SaveAsync(string conversationId, IReadOnlyList<ConversationMessage> messages)
    {
        if (messages.Count == 0) return;

        var partitionKey = new PartitionKey(conversationId);

        foreach (var msg in messages)
        {
            var content = msg.Type switch
            {
                "text" => msg.Content ?? "",
                "tool_call" => JsonSerializer.Serialize(new { tool = msg.ToolName, arguments = msg.ToolArguments }),
                "tool_call_response" => JsonSerializer.Serialize(new { tool = msg.ToolName, result = msg.ToolResult }),
                _ => msg.Content ?? ""
            };

            content = SanitizeForCosmos(content);

            var doc = new VoiceConversationDocument
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversationId,
                Timestamp = msg.Timestamp.ToUnixTimeSeconds(),
                Role = msg.Role,
                Message = content,
                Type = "ChatMessage",
                Ttl = 86400 * 7
            };

            try
            {
                await _container.CreateItemAsync(doc, partitionKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving conversation message to Cosmos");
            }
        }

        _logger.LogInformation("Saved {Count} conversation messages to Cosmos for {ConversationId}",
            messages.Count, conversationId);
    }

    /// <summary>
    /// Replaces non-ASCII characters with ASCII equivalents to avoid
    /// "unsupported Unicode escape sequence" errors in the Cosmos DB emulator.
    /// </summary>
    private static string SanitizeForCosmos(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\u2019' or '\u2018' => '\'',
                '\u201C' or '\u201D' => '"',
                '\u2013' or '\u2014' => '-',
                '\u2026' => '.',
                '\u00A0' => ' ',
                _ when c > 127 => ' ',
                _ => c
            });
        }
        return sb.ToString();
    }

    private sealed class VoiceConversationDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("conversationId")]
        public string ConversationId { get; set; } = "";

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("ttl")]
        public int Ttl { get; set; }
    }
}
