using System.Collections.Concurrent;
using System.Threading.Channels;
using PidorBot.AI;
using PidorBot.Brain;

namespace PidorBot.Chats;

/// <summary>
/// The bot in one chat: remembers the conversation, decides when to reply, replies, and talks on its own when the chat is quiet.
/// Replies in a chat never overlap: messages that arrive while the bot is replying are considered right after.
/// </summary>
public sealed partial class ChatSession : IAsyncDisposable
{
    public const int HistoryToLoad = 200;
    private const int MaxHistory = 400;

    // What the bot sees when replying. Older stuff comes from the memory.
    private const int ContextMessages = 10;
    private const int ContextCharacters = 3000;
    private static readonly TimeSpan _contextMaxAge = TimeSpan.FromHours(12);

    private static readonly TimeSpan _minSilenceBeforeTalking = TimeSpan.FromHours(6);
    private static readonly TimeSpan _maxSilenceBeforeTalking = TimeSpan.FromHours(24);
    private const int NightStartsAt = 1;
    private const int NightEndsAt = 9;

    // The bot looks only at the latest photos.
    private const int PhotosToLook = 3;
    private const int MaxCachedPhotos = 20;
    private const string GreetingSticker = "CAACAgIAAxkBAAPmZkjEDUlcu_qvm1AR_v4oHF_gZ-8AAmMGAAJuMtgAAR89SJYjCuEgNQQ";

    private readonly ChatServices _services;
    private readonly ILogger<ChatSession> _logger;
    private readonly ReplyPolicy _policy;
    private readonly Lock _lock = new();
    private readonly List<ChatMessage> _history;
    private readonly SemaphoreSlim _replying = new(1, 1);
    private readonly Channel<bool> _newMessages = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, BinaryData> _photos = new();
    private Task _loop = Task.CompletedTask;

    // Messages are counted from the start of the session, including the ones removed from the history.
    private long _removedMessages;
    private long _seenMessages;

    private BotMode _mode;
    private DateTime _contextStartsAt = DateTime.MinValue;
    private DateTime _lastKeywordReplyAt = DateTime.MinValue;
    private int _repliesSinceVoice;
    private DateTime _talkAt;
    private bool _lastMessageIsSpontaneous;
    private volatile bool _canWrite = true;

    private ChatSession(ChatRef chat, ChatServices services, IReadOnlyList<ChatMessage> history, BotMode mode)
    {
        Chat = chat;
        _services = services;
        _logger = services.Loggers.CreateLogger<ChatSession>();
        _policy = new ReplyPolicy(services.Bot.Username);
        _history = [.. history];
        _seenMessages = _history.Count;
        _mode = mode;

        // Don't talk to everyone right after a restart.
        var now = Now;
        var lastMessageAt = _history.Count > 0 ? _history[^1].Date : now;
        var talkAt = lastMessageAt + RandomSilence();
        var earliest = now + TimeSpan.FromHours(1) + ((RandomSilence() - _minSilenceBeforeTalking) / 4);
        _talkAt = talkAt > earliest ? talkAt : earliest;
    }

    public ChatRef Chat { get; }
    public BotMode Mode => _mode;

    private DateTime Now => _services.Time.GetUtcNow().UtcDateTime;
    private DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(_services.Time.GetUtcNow(), _services.Settings.TimeZone);
    private CancellationToken Stopping => _cts.Token;

    public static async Task<ChatSession> CreateAsync(ChatRef chat, ChatServices services)
    {
        var history = await services.Store.GetHistoryAsync(chat, HistoryToLoad);
        var mode = Modes.Find(await services.Store.GetModeAsync(chat)) ?? Modes.Default;

        return new ChatSession(chat, services, history, mode);
    }

    /// <summary>
    /// Starts replying to messages. Not started in tests, they call <see cref="ReplyIfNeededAsync"/> directly.
    /// </summary>
    public void Start()
    {
        _loop = ReplyLoopAsync(Stopping);
        _services.Memory.Consider(Chat, GetHistory, Stopping);
    }

    public IReadOnlyList<ChatMessage> GetHistory()
    {
        lock (_lock)
            return [.. _history];
    }

    /// <param name="isBacklog">Message was sent while the bot was offline: remember it, but don't reply.</param>
    public async Task HandleAsync(ChatMessage message, bool isBacklog)
    {
        if (isBacklog)
        {
            await RememberAsync(message, isSeen: true);
            return;
        }

        // People write here, so the bot is still a member of the chat.
        _canWrite = true;

        if (await TryHandleCommandAsync(message))
            return;

        await RememberAsync(message, isSeen: false);
        _newMessages.Writer.TryWrite(true);
    }

    public void Greet(string invitedBy) => RunInBackground("greet the chat", async () =>
    {
        try
        {
            await _services.Messenger.SendStickerAsync(Chat, GreetingSticker, Stopping);
        }
        catch (Exception exception) when (exception is not ChatUnavailableException)
        {
            // Sticker ids are bot-specific, it doesn't work with the test bot.
            _logger.LogWarning(exception, "Could not send the greeting sticker");
        }

        await ReplyAsync(ReplyKind.Instructed, Prompts.Greeting(Chat.IsPrivate, invitedBy));
    });

    public Task RemindAsync(string requestedBy, string request)
        => ReplyAsync(ReplyKind.Instructed, Prompts.Reminder(requestedBy, request));

    internal Task RememberBotMessageAsync(string text)
        => RememberAsync(ChatMessage.FromBot(text, Now), isSeen: true);

    /// <summary>
    /// Called every minute: the bot thinks about the chat and talks if it has been quiet for a while.
    /// </summary>
    public void Tick()
    {
        _services.Memory.Consider(Chat, GetHistory, Stopping);

        var now = Now;
        if (Chat.IsPrivate || !_canWrite || now < _talkAt)
            return;

        var localNow = LocalNow;
        if (localNow.Hour is >= NightStartsAt and < NightEndsAt)
        {
            var morning = localNow.Date.AddHours(NightEndsAt) - localNow.DateTime;
            _talkAt = now + morning + TimeSpan.FromMinutes(_services.Random.Next(0, 180));
            return;
        }

        bool lastMessageIsSpontaneous;
        lock (_lock)
        {
            lastMessageIsSpontaneous = _lastMessageIsSpontaneous;
            _talkAt = now + RandomSilence();
        }

        // Don't talk to yourself: one message out of the blue is enough until someone answers.
        if (lastMessageIsSpontaneous)
            return;

        RunInBackground("talk on its own", async () =>
        {
            if (await ReplyAsync(ReplyKind.Spontaneous, null, waitForOtherReply: false))
            {
                lock (_lock)
                    _lastMessageIsSpontaneous = true;
            }
        });
    }

    /// <summary>
    /// Replies if new messages call for it. Called by the reply loop when messages arrive.
    /// </summary>
    public async Task ReplyIfNeededAsync()
    {
        ReplyReason reason;
        bool canReact;
        lock (_lock)
        {
            var now = Now;
            var newMessages = _history.Skip((int)Math.Max(0, _seenMessages - _removedMessages)).ToList();
            _seenMessages = _removedMessages + _history.Count;

            reason = _policy.Decide(newMessages, Chat.IsPrivate, now, _lastKeywordReplyAt);
            if (reason == ReplyReason.Keyword)
                _lastKeywordReplyAt = now;

            canReact = newMessages.Any(message => !message.IsFromBot && message.TelegramId != null);
        }

        if (reason == ReplyReason.None)
        {
            // Not addressed to the bot, but it reads the chat and sometimes reacts.
            if (canReact && _services.Random.NextDouble() < _services.Settings.GlanceChance)
                await ReplyAsync(ReplyKind.Glance, null, waitForOtherReply: false);

            return;
        }

        _logger.LogDebug("Replying in chat {ChatId}, reason: {Reason}", Chat, reason);
        await _services.Pacing.ReadAsync(Stopping);

        // Whatever was written while the bot was reading, it has read too.
        lock (_lock)
            _seenMessages = _removedMessages + _history.Count;

        await ReplyAsync(ReplyKind.Reply, null);
    }

    public async ValueTask DisposeAsync()
    {
        _newMessages.Writer.TryComplete();
        await _cts.CancelAsync();
        await _loop;
        _cts.Dispose();
        _replying.Dispose();
    }

    private async Task ReplyLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _newMessages.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ReplyIfNeededAsync();
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(exception, "Could not reply in chat {ChatId}", Chat);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <returns>Whether the bot has written something.</returns>
    private async Task<bool> ReplyAsync(ReplyKind kind, string? instruction, bool waitForOtherReply = true)
    {
        if (waitForOtherReply)
            await _replying.WaitAsync(Stopping);
        else if (!await _replying.WaitAsync(0, Stopping))
            return false;

        try
        {
            return await ComposeAndSendAsync(kind, instruction);
        }
        catch (ChatUnavailableException exception)
        {
            _canWrite = false;
            _logger.LogWarning(exception, "Can't write to chat {ChatId}", Chat);
            return false;
        }
        finally
        {
            _replying.Release();
        }
    }

    private async Task<bool> ComposeAndSendAsync(ReplyKind kind, string? instruction)
    {
        var context = GetContext();
        var isVoice = kind == ReplyKind.Reply && IsVoiceTurn();

        await using var turn = new ChatTurn(this, _services, context, isVoice, showTyping: kind != ReplyKind.Glance);

        var memory = await _services.Memory.GetAsync(Chat);
        var mood = await _services.Moods.GetAsync(Chat, _mode, GetHistory, Stopping);
        var request = new ReplyRequest(
            kind, _mode, mood, memory?.Text, context, LocalNow, instruction,
            await LookAtPhotosAsync(context),
            kind == ReplyKind.Glance ? null : await GetRemindersAsync());

        var tools = await GetToolsAsync(kind, context);
        try
        {
            await _services.Responder.RunAsync(request, tools, turn, Stopping);
        }
        catch (Exception exception) when (request.Photos?.Count > 0 && !turn.HasWritten && !Stopping.IsCancellationRequested)
        {
            // Don't let a photo the model can't handle make the bot mute.
            _logger.LogWarning(exception, "Could not reply with photos, replying without them");
            await _services.Responder.RunAsync(request with { Photos = null }, tools, turn, Stopping);
        }

        _logger.LogInformation("Finished {Kind} turn in chat {ChatId}, has written: {HasWritten}", kind, Chat, turn.HasWritten);
        return turn.HasWritten;
    }

    private async Task<IReadOnlyList<LlmTool>> GetToolsAsync(ReplyKind kind, IReadOnlyList<ChatMessage> context)
    {
        var tools = new List<LlmTool>();
        if (context.Any(message => message.TelegramId != null && !message.IsFromBot))
            tools.Add(BotTools.ReactTool);

        if (kind == ReplyKind.Glance)
            return tools;

        var stickerEmojis = await _services.Stickers.GetEmojisAsync(Chat, 60);
        if (stickerEmojis.Count > 0)
            tools.Add(BotTools.StickerTool(stickerEmojis));

        tools.AddRange([BotTools.ImageTool, BotTools.SetReminderTool, BotTools.CancelReminderTool]);
        return tools;
    }

    private async Task<IReadOnlyList<string>> GetRemindersAsync()
        => [.. (await _services.Reminders.GetAsync(Chat)).Select(reminder =>
            $"- id {ChatTurn.ShortId(reminder.Id)}: {BotTools.FormatLocalTime(TimeZoneInfo.ConvertTimeFromUtc(reminder.AtUtc, _services.Settings.TimeZone))}"
            + $"{(reminder.EveryDay ? " (every day)" : string.Empty)}, asked by {reminder.RequestedBy}: {reminder.Request}")];

    private async Task<IReadOnlyDictionary<string, BinaryData>> LookAtPhotosAsync(IReadOnlyList<ChatMessage> context)
    {
        var photos = new Dictionary<string, BinaryData>();
        foreach (var message in context.Where(message => message.PhotoFileId != null).TakeLast(PhotosToLook))
        {
            if (!_photos.TryGetValue(message.PhotoFileId!, out var photo))
            {
                try
                {
                    photo = BinaryData.FromBytes(await _services.Messenger.DownloadAsync(message.PhotoFileId!, Stopping));
                }
                catch (Exception exception) when (!Stopping.IsCancellationRequested)
                {
                    _logger.LogWarning(exception, "Could not download a photo");
                    continue;
                }

                if (_photos.Count >= MaxCachedPhotos)
                    _photos.Clear();

                _photos[message.PhotoFileId!] = photo;
            }

            photos[message.Id] = photo;
        }

        return photos;
    }

    private bool IsVoiceTurn()
    {
        if (!_services.Voice.IsEnabled)
            return false;

        lock (_lock)
        {
            if (++_repliesSinceVoice <= _services.Settings.VoiceEveryReplies)
                return false;

            _repliesSinceVoice = 0;
            return true;
        }
    }

    private List<ChatMessage> GetContext()
    {
        lock (_lock)
        {
            var startsAt = Now - _contextMaxAge;
            if (_contextStartsAt > startsAt)
                startsAt = _contextStartsAt;

            var context = new List<ChatMessage>();
            var characters = 0;
            for (var i = _history.Count - 1; i >= 0 && context.Count < ContextMessages; i--)
            {
                var message = _history[i];
                characters += message.Text.Length;
                if (message.Date < startsAt || (characters > ContextCharacters && context.Count > 0))
                    break;

                context.Add(message);
            }

            context.Reverse();
            return context;
        }
    }

    private async Task RememberAsync(ChatMessage message, bool isSeen)
    {
        lock (_lock)
        {
            var allSeen = _seenMessages == _removedMessages + _history.Count;

            _history.Add(message);
            if (_history.Count > MaxHistory)
            {
                var removed = _history.Count - HistoryToLoad;
                _history.RemoveRange(0, removed);
                _removedMessages += removed;
            }

            if (isSeen && allSeen)
                _seenMessages = _removedMessages + _history.Count;

            if (!message.IsFromBot)
                _lastMessageIsSpontaneous = false;

            _talkAt = message.Date + RandomSilence();
        }

        await _services.Store.AddToHistoryAsync(Chat, message);
        _services.Memory.Consider(Chat, GetHistory, Stopping);
    }

    private TimeSpan RandomSilence()
        => _minSilenceBeforeTalking + ((_maxSilenceBeforeTalking - _minSilenceBeforeTalking) * _services.Random.NextDouble());

    private void RunInBackground(string what, Func<Task> action) => _ = Task.Run(async () =>
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (Stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not {What} in chat {ChatId}", what, Chat);
        }
    }, CancellationToken.None);
}
