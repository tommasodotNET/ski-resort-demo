using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SharedServices;

public class CosmosThreadRepository : ICosmosThreadRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosThreadRepository> _logger;

    public CosmosThreadRepository(
        [FromKeyedServices("conversations")] Container container,
        ILogger<CosmosThreadRepository> logger)
    {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<JsonElement?> GetThreadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.ReadItemAsync<CosmosThreadItem>(
                key,
                new PartitionKey(key),
                cancellationToken: cancellationToken);

            var jsonElement = JsonSerializer.Deserialize<JsonElement>(response.Resource.SerializedThread);
            _logger.LogInformation("Successfully retrieved agent thread with key: {Key}", key);
            return jsonElement;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Agent thread not found for key: {Key}", key);
            return null;
        }
    }

    public async Task SaveThreadAsync(string key, JsonElement thread, CancellationToken cancellationToken = default)
    {
        var serializedThreadString = JsonSerializer.Serialize(thread);

        var threadItem = new CosmosThreadItem
        {
            Id = key,
            ConversationId = key,
            SerializedThread = serializedThreadString,
            LastUpdated = DateTime.UtcNow.ToString("o"),
            Ttl = -1
        };

        var requestOptions = new ItemRequestOptions
        {
            EnableContentResponseOnWrite = false
        };

        await _container.UpsertItemAsync(
            threadItem,
            new PartitionKey(key),
            requestOptions: requestOptions,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Successfully saved agent thread with key: {Key}", key);
    }

    private class CosmosThreadItem
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("conversationId")]
        public required string ConversationId { get; set; }

        [JsonPropertyName("serializedThread")]
        public required string SerializedThread { get; set; }

        [JsonPropertyName("lastUpdated")]
        public string LastUpdated { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonPropertyName("ttl")]
        public int? Ttl { get; set; }
    }
}
