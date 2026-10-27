using System.Globalization;
using PidorBot.Chats;

namespace PidorBot.Storage;

/// <summary>
/// Chats where the bot was activated with $activate. Same "allowed_chats" file as in the old FoulBot:
/// a JSON array of "groupChatId" and "privateChatId$botUsername" entries.
/// Entries of other old FoulBot bots are kept untouched.
/// </summary>
public sealed class AccessList : IDisposable
{
    private readonly ILogger<AccessList> _logger;
    private readonly string _path;
    private readonly string _botUsername;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<string>? _entries;

    public AccessList(ILogger<AccessList> logger, string path, string botUsername)
    {
        _logger = logger;
        _path = path;
        _botUsername = botUsername;
    }

    public async Task<bool> IsAllowedAsync(ChatRef chat)
    {
        var entries = await GetEntriesAsync();
        lock (entries)
            return entries.Contains(Key(chat));
    }

    /// <summary>
    /// Returns false when the chat was already allowed.
    /// </summary>
    public Task<bool> AllowAsync(ChatRef chat)
        => ChangeAsync(entries =>
        {
            if (entries.Contains(Key(chat)))
                return false;

            entries.Add(Key(chat));
            _logger.LogWarning("Allowed chat {ChatId}", chat);
            return true;
        });

    public Task<bool> DisallowAsync(ChatRef chat)
        => ChangeAsync(entries =>
        {
            var removed = entries.Remove(Key(chat));
            if (removed)
                _logger.LogWarning("Disallowed chat {ChatId}", chat);

            return removed;
        });

    public async Task<IReadOnlyList<ChatRef>> GetAllowedGroupsAsync()
    {
        var entries = await GetEntriesAsync();
        lock (entries)
        {
            return [.. entries
                .Select(entry => long.TryParse(entry, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) ? new ChatRef(id) : (ChatRef?)null)
                .OfType<ChatRef>()
                .Where(chat => !chat.IsPrivate)];
        }
    }

    public void Dispose() => _lock.Dispose();

    private string Key(ChatRef chat) => chat.IsPrivate ? $"{chat}${_botUsername}" : chat.ToString();

    private async Task<bool> ChangeAsync(Func<List<string>, bool> change)
    {
        var entries = await GetEntriesAsync();

        await _lock.WaitAsync();
        try
        {
            string serialized;
            lock (entries)
            {
                if (!change(entries))
                    return false;

                serialized = JsonSerializer.Serialize(entries);
            }

            // Write in place: the file is bind-mounted into the container, it can't be replaced by renaming.
            await File.WriteAllTextAsync(_path, serialized);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async ValueTask<List<string>> GetEntriesAsync()
    {
        if (_entries != null)
            return _entries;

        await _lock.WaitAsync();
        try
        {
            if (_entries != null)
                return _entries;

            if (!File.Exists(_path))
                await File.WriteAllTextAsync(_path, "[]");

            var content = await File.ReadAllTextAsync(_path);
            var entries = JsonSerializer.Deserialize<List<string>>(content)
                ?? throw new InvalidOperationException($"Could not read allowed chats from {_path}.");

            return _entries = [.. entries.Distinct()];
        }
        finally
        {
            _lock.Release();
        }
    }
}
