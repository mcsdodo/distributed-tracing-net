namespace Common.Redis;

public interface IRedisCacheService
{
    void Set(string key, string value);
    string? Get(string key);
}