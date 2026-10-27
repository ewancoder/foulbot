using System.Collections.Concurrent;
using PidorBot.Reminders;
using PidorBot.Storage;

namespace PidorBot.Chats;

/// <summary>
/// All the chats of the bot. The bot only works in chats where it was activated with $activate:
/// it's a private bot, people who just add it to a chat get nothing.
/// </summary>
public sealed class ChatHub : IAsyncDisposable
{
    public const string ActivateCommand = "$activate";
    public const string DeactivateCommand = "$deactivate";

    private readonly ILogger<ChatHub> _logger;
    private readonly ChatServices _services;
    private readonly AccessList _access;
    private readonly ConcurrentDictionary<ChatRef, ChatSession> _sessions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ChatHub(ILogger<ChatHub> logger, ChatServices services, AccessList access)
    {
        _logger = logger;
        _services = services;
        _access = access;
    }

    public async Task HandleMessageAsync(ChatRef chat, ChatMessage message, bool isBacklog)
    {
        var command = message.Text.Trim();
        if (!isBacklog && command == ActivateCommand)
        {
            if (await _access.AllowAsync(chat))
                (await GetSessionAsync(chat)).Greet(message.SenderName);

            return;
        }

        if (!await _access.IsAllowedAsync(chat))
        {
            _logger.LogTrace("Message from not allowed chat {ChatId}", chat);
            return;
        }

        if (!isBacklog && command == DeactivateCommand)
        {
            await _access.DisallowAsync(chat);
            await CloseSessionAsync(chat);
            return;
        }

        await (await GetSessionAsync(chat)).HandleAsync(message, isBacklog);
    }

    /// <summary>
    /// The bot was added to or removed from a chat.
    /// </summary>
    public async Task HandleMembershipAsync(ChatRef chat, bool isMember, string by)
    {
        _logger.LogWarning("Bot was {Action} chat {ChatId} by {By}", isMember ? "added to" : "removed from", chat, by);

        if (!isMember)
        {
            await CloseSessionAsync(chat);
            return;
        }

        if (await _access.IsAllowedAsync(chat))
            (await GetSessionAsync(chat)).Greet(by);
    }

    public async Task RemindAsync(Reminder reminder)
    {
        if (!await _access.IsAllowedAsync(reminder.Chat))
        {
            _logger.LogWarning("Skipping reminder {@Reminder}, the chat is not allowed anymore", reminder);
            return;
        }

        await (await GetSessionAsync(reminder.Chat)).RemindAsync(reminder.RequestedBy, reminder.Request);
    }

    /// <summary>
    /// Wakes up the bot in all allowed group chats, so it can talk on its own after a restart.
    /// </summary>
    public async Task LoadGroupsAsync()
    {
        foreach (var chat in await _access.GetAllowedGroupsAsync())
            await GetSessionAsync(chat);

        _logger.LogInformation("Loaded {Count} chats", _sessions.Count);
    }

    public void Tick()
    {
        foreach (var session in _sessions.Values)
            session.Tick();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_sessions.Values.Select(session => session.DisposeAsync().AsTask()));
        _sessions.Clear();
        _lock.Dispose();
    }

    private async Task<ChatSession> GetSessionAsync(ChatRef chat)
    {
        if (_sessions.TryGetValue(chat, out var session))
            return session;

        await _lock.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(chat, out session))
                return session;

            session = await ChatSession.CreateAsync(chat, _services);
            session.Start();
            _sessions[chat] = session;

            _logger.LogInformation("Started chat {ChatId} in {Mode} mode", chat, session.Mode.Name);
            return session;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task CloseSessionAsync(ChatRef chat)
    {
        if (_sessions.TryRemove(chat, out var session))
            await session.DisposeAsync();
    }
}
