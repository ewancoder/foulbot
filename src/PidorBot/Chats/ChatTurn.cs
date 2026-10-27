using PidorBot.Brain;

namespace PidorBot.Chats;

/// <summary>
/// One turn of the bot in a chat: carries out what the LLM decided to do.
/// </summary>
internal sealed class ChatTurn : IChatActions, IAsyncDisposable
{
    private const int MaxReactions = 3;
    private const int MaxImages = 1;

    private readonly ChatSession _session;
    private readonly ChatServices _services;
    private readonly IReadOnlyList<ChatMessage> _context;
    private bool _isVoice;
    private ITyping? _typing;
    private int _reactions;
    private int _images;

    public ChatTurn(ChatSession session, ChatServices services, IReadOnlyList<ChatMessage> context, bool isVoice, bool showTyping)
    {
        _session = session;
        _services = services;
        _context = context;
        _isVoice = isVoice;
        _typing = showTyping ? services.Pacing.StartTyping(session.Chat, isVoice, CancellationToken.None) : null;
    }

    /// <summary>
    /// Whether the bot has written or sent something to the chat (reactions don't count).
    /// </summary>
    public bool HasWritten { get; private set; }

    private ChatRef Chat => _session.Chat;

    public async Task SayAsync(string text, bool remember, CancellationToken cancellationToken)
    {
        // Only the first message of the turn can be a voice message.
        var voice = _isVoice ? await _services.Voice.SpeakAsync(text, cancellationToken) : null;
        _isVoice = false;

        if (_typing != null)
            await _typing.FinishAsync(text, cancellationToken);
        await StopTypingAsync();

        if (voice != null)
        {
            await using (voice)
                await _services.Messenger.SendVoiceAsync(Chat, voice, cancellationToken);
        }
        else
        {
            await _services.Messenger.SendTextAsync(Chat, text, cancellationToken);
        }

        HasWritten = true;

        // Replies where the bot broke the character are not remembered, so it doesn't imitate them.
        if (remember)
            await _session.RememberBotMessageAsync(text);
    }

    public async Task<string> ReactAsync(int messageId, string emoji, CancellationToken cancellationToken)
    {
        if (!_context.Any(message => message.TelegramId == messageId && !message.IsFromBot))
            return $"Error: there is no message {messageId}.";

        if (!BotTools.ReactionEmojis.Contains(emoji))
            return $"Error: {emoji} can't be used as a reaction.";

        if (++_reactions > MaxReactions)
            return "Error: enough reactions for now.";

        await _services.Messenger.ReactAsync(Chat, messageId, emoji, cancellationToken);
        return "Done.";
    }

    public async Task<string> SendStickerAsync(string emoji, CancellationToken cancellationToken)
    {
        var sticker = await _services.Stickers.PickAsync(emoji);
        if (sticker == null)
            return "Error: you don't have any stickers yet.";

        await StopTypingAsync();
        await _services.Messenger.SendStickerAsync(Chat, sticker.FileId, cancellationToken);
        HasWritten = true;

        return $"Sent a sticker {sticker.Emoji}.";
    }

    public async Task<string> SendImageAsync(string prompt, string? caption, CancellationToken cancellationToken)
    {
        if (++_images > MaxImages)
            return "Error: one picture at a time.";

        var image = await _services.Images.GenerateAsync(prompt, cancellationToken);
        if (image == null)
            return "Error: the picture didn't work out (the image model refused or failed). Say it in your own words if you want.";

        await StopTypingAsync();
        await _services.Messenger.SendPhotoAsync(Chat, image, caption, cancellationToken);
        HasWritten = true;

        if (!string.IsNullOrWhiteSpace(caption))
            await _session.RememberBotMessageAsync(caption);

        return "The picture is sent.";
    }

    public async Task<string> SetReminderAsync(DateTime localTime, string what, bool daily, CancellationToken cancellationToken)
    {
        var timeZone = _services.Settings.TimeZone;
        var atUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), timeZone);
        var now = _services.Time.GetUtcNow().UtcDateTime;

        if (atUtc <= now)
        {
            if (!daily)
                return $"Error: {BotTools.FormatLocalTime(localTime)} is in the past.";

            while (atUtc <= now)
                atUtc = atUtc.AddDays(1);
        }

        var requestedBy = _context.LastOrDefault(message => !message.IsFromBot)?.SenderName ?? "someone";
        var reminder = await _services.Reminders.AddAsync(Chat, atUtc, what, daily, requestedBy);

        return $"Reminder {ShortId(reminder.Id)} is set for {BotTools.FormatLocalTime(TimeZoneInfo.ConvertTimeFromUtc(atUtc, timeZone))}.";
    }

    public async Task<string> CancelReminderAsync(string id, CancellationToken cancellationToken)
    {
        var reminder = (await _services.Reminders.GetAsync(Chat))
            .FirstOrDefault(reminder => reminder.Id.StartsWith(id.Trim(), StringComparison.OrdinalIgnoreCase));

        if (reminder == null || id.Trim().Length == 0)
            return $"Error: there is no reminder {id}.";

        await _services.Reminders.RemoveAsync(Chat, reminder.Id);
        return $"Reminder {ShortId(reminder.Id)} is cancelled.";
    }

    public ValueTask DisposeAsync() => new(StopTypingAsync());

    public static string ShortId(string id) => id.Length > 8 ? id[..8] : id;

    private async Task StopTypingAsync()
    {
        if (_typing == null)
            return;

        await _typing.DisposeAsync();
        _typing = null;
    }
}
