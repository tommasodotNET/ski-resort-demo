using System.Text.Json;

namespace SharedServices;

public interface ICosmosThreadRepository
{
    Task<JsonElement?> GetThreadAsync(string key, CancellationToken cancellationToken = default);
    Task SaveThreadAsync(string key, JsonElement thread, CancellationToken cancellationToken = default);
}
