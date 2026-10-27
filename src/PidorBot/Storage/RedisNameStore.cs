using System.Collections.Concurrent;
using StackExchange.Redis;

namespace PidorBot.Storage;

/// <summary>
/// Read-only "hard_names" key of the old FoulBot: JSON object { username: name }, or the older "username=name,..." format.
/// </summary>
public sealed class RedisNameStore : INameStore, IDisposable
{
    private const string Key = "hard_names";

    private readonly ILogger<RedisNameStore> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConcurrentDictionary<string, string>? _names;

    public RedisNameStore(ILogger<RedisNameStore> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async Task<string?> GetNameAsync(string username)
    {
        var names = await GetNamesAsync();

        return names.TryGetValue(username, out var name) ? name : null;
    }

    public void Dispose() => _lock.Dispose();

    internal static IEnumerable<KeyValuePair<string, string>> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        if (value.StartsWith('{'))
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? [];

        // Legacy "username=name,username=name" format. Skip malformed entries instead of failing.
        return value
            .Split(',')
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .DistinctBy(parts => parts[0])
            .Select(parts => new KeyValuePair<string, string>(parts[0], parts[1]));
    }

    private async ValueTask<ConcurrentDictionary<string, string>> GetNamesAsync()
    {
        if (_names != null)
            return _names;

        await _lock.WaitAsync();
        try
        {
            if (_names != null)
                return _names;

            var value = await _redis.GetDatabase().StringGetAsync(Key);

            return _names = new(Parse(value.ToString()));
        }
        catch (Exception exception)
        {
            // Don't cache: try again with the next message.
            _logger.LogError(exception, "Could not load custom names from Redis");
            return new();
        }
        finally
        {
            _lock.Release();
        }
    }
}
