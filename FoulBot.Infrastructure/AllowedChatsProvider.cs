using System.Collections.Concurrent;
using FoulBot.Domain.Storage;

namespace FoulBot.Infrastructure;

// TODO: Move this to Infrastructure as this is an implementation detail.
public sealed class AllowedChatsProvider : IAllowedChatsProvider, IDisposable
{
    private readonly ILogger<AllowedChatsProvider> _logger;
    private readonly string _fileName;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConcurrentDictionary<string, bool>? _allowedChats;

    public AllowedChatsProvider(
        ILogger<AllowedChatsProvider> logger,
        string fileName = "allowed_chats")
    {
        _fileName = fileName;
        _logger = logger;
    }

    public async ValueTask<IEnumerable<FoulChatId>> GetAllAllowedChatsAsync()
    {
        var chats = await GetAllowedChatsAsync();

        return chats.Keys.Select(GetFoulChatId).ToList();
    }

    public async ValueTask<bool> IsAllowedChatAsync(FoulChatId chatId)
    {
        var allowedChats = await GetAllowedChatsAsync();

        return allowedChats.ContainsKey(GetKey(chatId));
    }

    public async ValueTask AllowChatAsync(FoulChatId chatId)
    {
        _logger.LogWarning("Allowing chat {ChatId}", chatId);

        var allowedChats = await GetAllowedChatsAsync();
        allowedChats.TryAdd(GetKey(chatId), false);

        await SaveChangesAsync();
    }

    public async ValueTask DisallowChatAsync(FoulChatId chatId)
    {
        _logger.LogWarning("Disallowing chat {ChatId}", chatId);

        var allowedChats = await GetAllowedChatsAsync();
        allowedChats.TryRemove(GetKey(chatId), out _);

        await SaveChangesAsync();
    }

    private async ValueTask SaveChangesAsync()
    {
        var allowedChats = await GetAllowedChatsAsync();

        var serialized = JsonSerializer.Serialize(allowedChats.Keys);

        await _lock.WaitAsync();
        try
        {
            _logger.LogTrace("Acquired lock for saving allowed chats");
            _logger.LogDebug("Saving list of allowed chats: {SerializedAllowedChats}", serialized);
            await File.WriteAllTextAsync(_fileName, serialized);
        }
        finally
        {
            _logger.LogTrace("Releasing lock for saving allowed chats");
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private async ValueTask<ConcurrentDictionary<string, bool>> GetAllowedChatsAsync()
    {
        if (_allowedChats != null)
            return _allowedChats;

        await _lock.WaitAsync();
        try
        {
            _logger.LogTrace("Acquired lock for initially reading allowed chats");
#pragma warning disable CA1508 // False positive: It is updated below.
            if (_allowedChats != null)
            {
                _logger.LogTrace("Allowed chats were populated by another thread. Skipping");
                return _allowedChats;
            }
#pragma warning restore CA1508

            if (!File.Exists(_fileName))
            {
                _logger.LogTrace("Allowed chats file was not created. Creating");
                await File.WriteAllTextAsync(_fileName, "[]");
            }

            var fileContent = await File.ReadAllTextAsync(_fileName);
            var chats = JsonSerializer.Deserialize<string[]>(fileContent)?.Distinct()
                ?? throw new InvalidOperationException("Failed to deserialize allowed chats.");

            _logger.LogDebug("Finished reading allowed chats from file");
            _allowedChats = new ConcurrentDictionary<string, bool>(
                chats.Select(chat => new KeyValuePair<string, bool>(GetKey(new(chat)), false)));

            return _allowedChats;
        }
        finally
        {
            _logger.LogTrace("Releasing lock for initially reading allowed chats");
            _lock.Release();
        }
    }

    private static string GetKey(FoulChatId chatId)
        => chatId.IsPrivate
            ? $"{chatId.Value}${chatId.FoulBotId?.BotId}"
            : chatId.Value;

    private static FoulChatId GetFoulChatId(string key)
        => key.Contains('$')
            ? new(key.Split('$')[0]) { FoulBotId = new(key.Split('$')[1], "Unknown") }
            : new(key);
}
