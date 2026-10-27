using System.Collections.Concurrent;
using PidorBot.Chats;
using StackExchange.Redis;

namespace PidorBot.Storage;

public sealed record KnownSticker(string FileId, string UniqueId, string Emoji, string? Set);

public interface IStickerBook
{
    Task<bool> KnowsSetAsync(string set);
    Task LearnAsync(IReadOnlyCollection<KnownSticker> stickers);

    /// <summary>
    /// Emojis the bot has stickers for, the most common first.
    /// </summary>
    Task<IReadOnlyList<string>> GetEmojisAsync(int count);

    /// <summary>
    /// Random sticker with this emoji, or any sticker if there are none with it.
    /// </summary>
    Task<KnownSticker?> PickAsync(string emoji);
}

/// <summary>
/// Stickers the bot can send: it learns them from the stickers people send (and their whole sticker sets).
/// File ids work only for the bot that received them, so the catalog is per bot.
/// </summary>
public sealed class StickerBook : IStickerBook, IDisposable
{
    public const int MaxStickers = 2000;

    private readonly ILogger<StickerBook> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly Random _random;
    private readonly string _key;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConcurrentDictionary<string, KnownSticker>? _stickers;

    public StickerBook(ILogger<StickerBook> logger, IConnectionMultiplexer redis, BotIdentity bot, Random random)
    {
        _logger = logger;
        _redis = redis;
        _random = random;
        _key = $"pidorbot:{bot.Username}:stickers";
    }

    public async Task<bool> KnowsSetAsync(string set)
        => (await GetAsync()).Values.Any(sticker => sticker.Set == set);

    public async Task LearnAsync(IReadOnlyCollection<KnownSticker> stickers)
    {
        var known = await GetAsync();
        var learned = stickers
            .Where(sticker => known.Count < MaxStickers && known.TryAdd(sticker.UniqueId, sticker))
            .ToList();

        if (learned.Count == 0)
            return;

        try
        {
            await _redis.GetDatabase().HashSetAsync(_key, [.. learned.Select(sticker
                => new HashEntry(sticker.UniqueId, JsonSerializer.Serialize(sticker)))]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save stickers to Redis");
        }
    }

    public async Task<IReadOnlyList<string>> GetEmojisAsync(int count)
        => [.. (await GetAsync()).Values
            .GroupBy(sticker => sticker.Emoji)
            .OrderByDescending(group => group.Count())
            .Take(count)
            .Select(group => group.Key)];

    public async Task<KnownSticker?> PickAsync(string emoji)
    {
        var stickers = (await GetAsync()).Values.ToList();
        var matching = stickers.Where(sticker => sticker.Emoji == emoji).ToList();
        var candidates = matching.Count > 0 ? matching : stickers;

        return candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
    }

    public void Dispose() => _lock.Dispose();

    private async ValueTask<ConcurrentDictionary<string, KnownSticker>> GetAsync()
    {
        if (_stickers != null)
            return _stickers;

        await _lock.WaitAsync();
        try
        {
            if (_stickers != null)
                return _stickers;

            var entries = await _redis.GetDatabase().HashGetAllAsync(_key);
            return _stickers = new(entries
                .Select(entry => JsonSerializer.Deserialize<KnownSticker>(entry.Value.ToString()))
                .OfType<KnownSticker>()
                .Select(sticker => KeyValuePair.Create(sticker.UniqueId, sticker)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load stickers from Redis");
            return new();
        }
        finally
        {
            _lock.Release();
        }
    }
}
