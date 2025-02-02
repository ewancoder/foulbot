using System.Collections.Concurrent;
using StackExchange.Redis;

namespace FoulBot.Infrastructure.Storage;

public sealed class RedisNamesStorage : INamesStorage
{
    private readonly IConnectionMultiplexer _redis;
    private ConcurrentDictionary<string, string> _names = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized = false;

    public RedisNamesStorage(
        IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async ValueTask<string?> GetNameAsync(string nickname)
    {
        await InitializeAsync();

        if (_names.TryGetValue(nickname, out var name))
            return name;

        return null;
    }

    public async ValueTask SetNameAsync(string nickname, string name)
    {
        await InitializeAsync();

        _names.AddOrUpdate(nickname, name, (_, _) => name);

        var db = _redis.GetDatabase();
        var namesString = string.Join(',', _names.Select(x => $"{x.Key}={x.Value}"));
        await db.StringSetAsync("hard_names", namesString);
    }

    private async ValueTask InitializeAsync()
    {
        if (_initialized)
            return;

        await _lock.WaitAsync();
        try
        {
            if (_initialized)
                return;

            var db = _redis.GetDatabase();
            var namesString = await db.StringGetAsync("hard_names");
            var names = namesString.ToString()
                .Split(',')
                .Select(keyValue => new KeyValuePair<string, string>(keyValue.Split('=')[0], keyValue.Split('=')[1]));
            _names = new ConcurrentDictionary<string, string>(names);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }
}
