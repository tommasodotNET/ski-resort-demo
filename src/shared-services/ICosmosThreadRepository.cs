namespace SharedServices;

public interface ICosmosThreadRepository
{
    Task<string?> GetThreadAsync(string key);
    Task SaveThreadAsync(string key, string serializedSession);
}
