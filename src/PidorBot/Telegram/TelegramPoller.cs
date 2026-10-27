using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using PidorBot.Chats;
using PidorBot.Storage;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UnidecodeSharpCore;

namespace PidorBot.Telegram;

/// <summary>
/// Receives updates from Telegram and passes them to the chats.
/// </summary>
public sealed partial class TelegramPoller : BackgroundService, IUpdateHandler
{
    // Messages sent while the bot was offline are remembered, but not replied to.
    private static readonly TimeSpan _backlogThreshold = TimeSpan.FromMinutes(1);

    private readonly ILogger<TelegramPoller> _logger;
    private readonly ITelegramBotClient _client;
    private readonly ChatHub _hub;
    private readonly INameStore _names;
    private readonly IStickerBook _stickers;
    private readonly BotIdentity _bot;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public TelegramPoller(
        ILogger<TelegramPoller> logger,
        ITelegramBotClient client,
        ChatHub hub,
        INameStore names,
        IStickerBook stickers,
        BotIdentity bot)
    {
        _logger = logger;
        _client = client;
        _hub = hub;
        _names = names;
        _stickers = stickers;
        _bot = bot;
    }

    [GeneratedRegex("[^a-zA-Z_]", RegexOptions.None, matchTimeoutMilliseconds: 50)]
    private static partial Regex NotAllowedNameCharacters();

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["UpdateId"] = update.Id,
            ["ChatId"] = update.Message?.Chat.Id ?? update.MyChatMember?.Chat.Id,
            ["ChatName"] = update.Message?.Chat.Title ?? update.Message?.Chat.Username ?? update.MyChatMember?.Chat.Title
        });

        try
        {
            if (update.Message is { } message)
                await HandleMessageAsync(message, cancellationToken);
            else if (update.MyChatMember is { } member)
                await HandleMembershipAsync(member);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not handle update {@Update}", update);
        }
    }

    public async Task HandleErrorAsync(
        ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        // Telegram has short downtimes every now and then.
        if (exception is ApiRequestException or RequestException or HttpRequestException)
            _logger.LogWarning(exception, "Telegram error ({Source})", source);
        else
            _logger.LogError(exception, "Telegram error ({Source})", source);

        if (source == HandleErrorSource.PollingError)
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Receiving Telegram updates as @{Username}", _bot.Username);

        return _client.ReceiveAsync(
            this,
            new ReceiverOptions { AllowedUpdates = [UpdateType.Message, UpdateType.MyChatMember] },
            stoppingToken);
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.From == null || message.From.IsBot)
            return;

        if (message.Sticker != null)
            await LearnStickersAsync(new ChatRef(message.Chat.Id), message.Sticker, cancellationToken);

        var text = Describe(message);
        if (text == null)
        {
            _logger.LogDebug("Skipping message of type {Type}", message.Type);
            return;
        }

        var chatMessage = new ChatMessage(
            $"tg-{message.Chat.Id}-{message.Id}",
            await GetNameAsync(message.From),
            message.From.Username,
            text,
            message.Date,
            IsFromBot: false,
            IsReplyToBot: message.ReplyToMessage?.From?.Id == _bot.Id)
        {
            TelegramId = message.Id,
            // The biggest size is too much for the LLM, the second biggest is enough.
            PhotoFileId = message.Photo is { Length: > 0 } photo ? photo[Math.Max(0, photo.Length - 2)].FileId : null
        };

        await _hub.HandleMessageAsync(new ChatRef(message.Chat.Id), chatMessage, isBacklog: message.Date < _startedAt - _backlogThreshold);
    }

    private async Task HandleMembershipAsync(ChatMemberUpdated member)
    {
        var isMember = member.NewChatMember.Status is not (ChatMemberStatus.Left or ChatMemberStatus.Kicked);

        await _hub.HandleMembershipAsync(new ChatRef(member.Chat.Id), isMember, await GetNameAsync(member.From));
    }

    /// <summary>
    /// Text for the LLM. Media is described, so the bot knows what was sent.
    /// </summary>
    private static string? Describe(Message message)
    {
        if (message.Text != null)
            return message.Text;

        var media = message switch
        {
            { Photo: not null } => "[фото]",
            { Sticker: { } sticker } => $"[стикер {sticker.Emoji}]",
            { Animation: not null } => "[гифка]",
            { Video: not null } => "[видео]",
            { VideoNote: not null } => "[видеосообщение]",
            { Voice: not null } => "[голосовое сообщение]",
            { Audio: { } audio } => $"[музыка: {audio.Performer} - {audio.Title}]",
            { Document: { } document } => $"[файл {document.FileName}]",
            { Location: not null } => "[геолокация]",
            { Poll: { } poll } => $"[опрос: {poll.Question}]",
            _ => null
        };

        if (media == null)
            return message.Caption;

        return message.Caption == null ? media : $"{media} {message.Caption}";
    }

    /// <summary>
    /// The bot learns every sticker set people use in a chat, so it has stickers to send there.
    /// </summary>
    private async Task LearnStickersAsync(ChatRef chat, Sticker sticker, CancellationToken cancellationToken)
    {
        try
        {
            var isNewSet = sticker.SetName != null && !await _stickers.KnowsSetAsync(chat, sticker.SetName);
            await _stickers.LearnAsync(chat, [ToKnown(sticker)]);

            if (isNewSet)
            {
                var set = await _client.GetStickerSet(sticker.SetName!, cancellationToken);
                await _stickers.LearnAsync(chat, [.. set.Stickers.Select(ToKnown)]);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not learn stickers from set {Set}", sticker.SetName);
        }

        static KnownSticker ToKnown(Sticker sticker)
            => new(sticker.FileId, sticker.FileUniqueId, sticker.Emoji ?? "🙂", sticker.SetName);
    }

    /// <summary>
    /// Same names as in the old FoulBot (like Ivan_Petrov), so the history and the memory stay consistent.
    /// </summary>
    private async Task<string> GetNameAsync(User user)
    {
        if (user.Username != null && await _names.GetNameAsync(user.Username) is { } customName)
            return customName;

        var name = new StringBuilder();
        foreach (var part in new[] { user.FirstName, user.LastName })
        {
            var clean = NotAllowedNameCharacters().Replace((part ?? string.Empty).Unidecode(), string.Empty);
            if (clean.Length > 0)
                name.Append(name.Length > 0 ? "_" : string.Empty).Append(clean);
        }

        if (name.Length == 0 && user.Username != null)
            name.Append(NotAllowedNameCharacters().Replace(user.Username.Unidecode(), string.Empty));

        return name.Length > 0 ? name.ToString() : $"User{user.Id}";
    }
}
