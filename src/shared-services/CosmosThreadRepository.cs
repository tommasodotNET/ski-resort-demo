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
        _container = container;
        _logger = logger;
    }

    public async Task<string?> GetThreadAsync(string key)
    {
        try
        {
            _logger.LogInformation("Getting thread with key: {Key}", key);
            
            var response = await _container.ReadItemAsync<dynamic>(
                id: key,
                partitionKey: new PartitionKey(key));

            var data = response.Resource.data?.ToString();
            _logger.LogInformation("Successfully retrieved thread with key: {Key}", key);
            
            return data;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Thread not found with key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting thread with key: {Key}", key);
            throw;
        }
    }

    public async Task SaveThreadAsync(string key, string serializedSession)
    {
        try
        {
            _logger.LogInformation("Saving thread with key: {Key}", key);
            
            var item = new
            {
                id = key,
                data = serializedSession
            };

            await _container.UpsertItemAsync(
                item: item,
                partitionKey: new PartitionKey(key));

            _logger.LogInformation("Successfully saved thread with key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving thread with key: {Key}", key);
            throw;
        }
    }
}
