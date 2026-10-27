using System.Collections.Concurrent;
using PidorBot.Chats;
using StackExchange.Redis;

namespace PidorBot.Storage;

public sealed record KnownSticker(string FileId, string UniqueId, string Emoji, string? Set);

public interface IStickerBook
{
    Task<bool> KnowsSetAsync(ChatRef chat, string set);
    Task LearnAsync(ChatRef chat, IReadOnlyCollection<KnownSticker> stickers);

    /// <summary>
    /// Emojis the bot has stickers for in this chat, the most common first.
    /// </summary>
    Task<IReadOnlyList<string>> GetEmojisAsync(ChatRef chat, int count);

    /// <summary>
    /// Random sticker of this chat with this emoji, or any sticker of this chat if there are none with it.
    /// </summary>
    Task<KnownSticker?> PickAsync(ChatRef chat, string emoji);
}

/// <summary>
/// Stickers the bot can send: it learns them from the stickers people send (and their whole sticker sets).
/// Each chat has its own catalog: the bot sends only the stickers it learned in that chat.
/// File ids work only for the bot that received them, so the catalogs are per bot.
/// </summary>
public sealed class StickerBook : IStickerBook, IDisposable
{
    public const int MaxStickers = 2000;

    private readonly ILogger<StickerBook> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly Random _random;
    private readonly string _keyPrefix;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<ChatRef, ConcurrentDictionary<string, KnownSticker>> _stickers = new();

    public StickerBook(ILogger<StickerBook> logger, IConnectionMultiplexer redis, BotIdentity bot, Random random)
    {
        _logger = logger;
        _redis = redis;
        _random = random;
        _keyPrefix = $"pidorbot:{bot.Username}:stickers";
    }

    public async Task<bool> KnowsSetAsync(ChatRef chat, string set)
        => (await GetAsync(chat)).Values.Any(sticker => sticker.Set == set);

    public async Task LearnAsync(ChatRef chat, IReadOnlyCollection<KnownSticker> stickers)
    {
        var known = await GetAsync(chat);
        var learned = stickers
            .Where(sticker => known.Count < MaxStickers && known.TryAdd(sticker.UniqueId, sticker))
            .ToList();

        if (learned.Count == 0)
            return;

        try
        {
            await _redis.GetDatabase().HashSetAsync(Key(chat), [.. learned.Select(sticker
                => new HashEntry(sticker.UniqueId, JsonSerializer.Serialize(sticker)))]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save stickers of chat {Chat} to Redis", chat.Id);
        }
    }

    public async Task<IReadOnlyList<string>> GetEmojisAsync(ChatRef chat, int count)
        => [.. (await GetAsync(chat)).Values
            .GroupBy(sticker => sticker.Emoji)
            .OrderByDescending(group => group.Count())
            .Take(count)
            .Select(group => group.Key)];

    public async Task<KnownSticker?> PickAsync(ChatRef chat, string emoji)
    {
        var stickers = (await GetAsync(chat)).Values.ToList();
        var matching = stickers.Where(sticker => sticker.Emoji == emoji).ToList();
        var candidates = matching.Count > 0 ? matching : stickers;

        return candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
    }

    public void Dispose() => _lock.Dispose();

    private string Key(ChatRef chat) => $"{_keyPrefix}:{chat.Id}";

    private async ValueTask<ConcurrentDictionary<string, KnownSticker>> GetAsync(ChatRef chat)
    {
        if (_stickers.TryGetValue(chat, out var cached))
            return cached;

        await _lock.WaitAsync();
        try
        {
            if (_stickers.TryGetValue(chat, out cached))
                return cached;

            var entries = await _redis.GetDatabase().HashGetAllAsync(Key(chat));
            return _stickers[chat] = new(entries
                .Select(entry => JsonSerializer.Deserialize<KnownSticker>(entry.Value.ToString()))
                .OfType<KnownSticker>()
                .Select(sticker => KeyValuePair.Create(sticker.UniqueId, sticker)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load stickers of chat {Chat} from Redis", chat.Id);
            return new();
        }
        finally
        {
            _lock.Release();
        }
    }
}
