using Google.Apis.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed record FoulBotConfiguration
{
    public FoulBotConfiguration(
        string botId,
        string botName,
        string directive,
        HashSet<string> keyWords)
    {
        if (botId == null || botName == null || directive == null || keyWords == null)
            throw new ArgumentException("One of the arguments is null.");

        if (!keyWords.Any())
            throw new ArgumentException("Should have at least one keyword.");

        BotId = botId;
        BotName = botName;
        Directive = directive;
        KeyWords = keyWords;
    }

    public string BotId { get; }
    public string BotName { get; }
    public string Directive { get; }
    public HashSet<string> KeyWords { get; }
    public int ContextSize { get; } = 20;
    public int ReplyEveryMessages { get; } = 20;
    public int MessagesBetweenVoice { get; init; } = 0;
    public bool UseOnlyVoice { get; init; } = false;
    public int BotOnlyMaxMessagesBetweenDebounce { get; init; } = 10;
    public int BotOnlyDecrementIntervalSeconds { get; init; } = 60;
    public bool NotAnAssistant { get; init; } = true;
    public HashSet<string> Stickers { get; } = new HashSet<string>();

    public FoulBotConfiguration WithVoiceBetween(int messages)
    {
        if (messages < 0)
            throw new InvalidOperationException("Messages count cannot be negative.");

        return this with
        {
            MessagesBetweenVoice = messages
        };
    }

    public FoulBotConfiguration WithOnlyVoice()
    {
        return this with
        {
            UseOnlyVoice = true
        };
    }

    public FoulBotConfiguration ConfigureBotToBotCommunication(
        int botOnlyMaxMessagesBetweenDebounce,
        int botOnlyDecrementIntervalSeconds)
    {
        if (botOnlyMaxMessagesBetweenDebounce < 0 || botOnlyDecrementIntervalSeconds < 0)
            throw new InvalidOperationException("Negative values are invalid.");

        return this with
        {
            BotOnlyMaxMessagesBetweenDebounce = botOnlyMaxMessagesBetweenDebounce,
            BotOnlyDecrementIntervalSeconds = botOnlyDecrementIntervalSeconds
        };
    }

    public FoulBotConfiguration DoNotTalkToBots()
    {
        return this with
        {
            BotOnlyMaxMessagesBetweenDebounce = 0
        };
    }

    public FoulBotConfiguration AllowBeingAnAssistant()
    {
        return this with
        {
            NotAnAssistant = false
        };
    }

    // TODO: Consider making it immutable.
    public FoulBotConfiguration AddStickers(params string[] stickers)
    {
        foreach (var sticker in stickers)
        {
            Stickers.Add(sticker);
        }

        return this;
    }
}

public interface IFoulBot
{
    ValueTask JoinChatAsync(string invitedBy);
}

public sealed class FoulBot : IFoulBot
{
    private static readonly string[] _failedContext = [
        "извините", "sorry", "простите", "не могу продолжать",
        "не могу участвовать", "давайте воздержимся", "прошу прощения",
        "прошу вас выражаться", "извини", "прости", "не могу помочь",
        "не могу обсуждать", "пои извинения", "нужно быть доброжелательным",
        "не думаю, что это хороший тон", "уважительным по отношению к другим",
        "поведения не терплю", "есть другие вопросы", "или нужна помощь",
        "stop it right there", "I'm here to help", "with any questions",
        "can assist", "не будем оскорблять", "не буду оскорблять", "не могу оскорблять",
        "нецензурн", "готов помочь", "с любыми вопросами", "за грубость"
    ];
    private static readonly string[] _failedContextCancellation = [
        "ссылок", "ссылк", "просматривать ссыл", "просматривать содерж", "просматривать контент"
    ];
    private readonly ILogger<FoulBot> _logger;
    private readonly IFoulAIClient _aiClient;
    private readonly ITelegramBotClient _botClient;
    private readonly FoulBotConfiguration _config;
    private readonly GoogleTtsService _googleTts = new GoogleTtsService();
    private readonly IFoulChat _chat;
    private readonly Random _random = new Random();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly Task _botOnlyDecrementTask;
    private readonly Task _repeatByTimeTask;
    private readonly int _botOnlyMaxCount = 10; // TODO: Consider moving to configuration.

    private int _randomReplyEveryMessages;
    private bool _subscribed = false;
    private int _botOnlyCount = 0;
    private int _audioCounter = 0;
    private int _replyEveryMessagesCounter = 0;
    private string? _lastProcessedId;

    // TODO: Consider whether I can make Chat a field of the constructor.
    public FoulBot(
        ILogger<FoulBot> logger,
        IFoulAIClient aiClient,
        ITelegramBotClient botClient,
        FoulBotConfiguration configuration,
        IFoulChat chat)
    {
        _logger = logger;
        _aiClient = aiClient;
        _botClient = botClient;
        _config = configuration;
        _chat = chat;
        _chat.StatusChanged += OnStatusChanged;

        using var _ = Logger.BeginScope();

        SetupNextReplyEveryMessages();

        _botOnlyDecrementTask = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.BotOnlyDecrementIntervalSeconds));

                if (_botOnlyCount > 0)
                {
                    _logger.LogTrace("Reduced bot-to-bot debounce to -1, current value {Value}", _botOnlyCount);
                    _botOnlyCount--;
                }
            }
        });

        _repeatByTimeTask = Task.Run(async () =>
        {
            while (true)
            {
                var waitMinutes = _random.Next(60, 720);
                if (waitMinutes < 120)
                    waitMinutes = _random.Next(60, 720);

                _logger.LogInformation("Prepairing to wait for a message based on time, {WaitMinutes} minutes.", waitMinutes);
                await Task.Delay(TimeSpan.FromMinutes(waitMinutes));
                _logger.LogInformation("Sending a message based on time, {WaitMinutes} minutes passed.", waitMinutes);
                await OnMessageReceivedAsync(FoulMessage.ByTime());
            }
        });
    }

    // TODO: Figure out how scope goes down here and whether I need to configure this at all.
    private IScopedLogger Logger => _logger
        .AddScoped("ChatId", _chat.ChatId)
        .AddScoped("BotId", _config.BotId);

    private void SetupNextReplyEveryMessages()
    {
        using var _ = Logger.BeginScope();

        _replyEveryMessagesCounter = 0;

        var count = _random.Next(Math.Max(1, _config.ReplyEveryMessages - 5), _config.ReplyEveryMessages + 15);
        _randomReplyEveryMessages = count;

        _logger.LogInformation("Reset next mandatory reply after {Count} messages.", count);
    }

    // Being called when bot has been invited to a group, or on the startup if chat is cached.
    public async ValueTask JoinChatAsync(string? invitedBy)
    {
        using var _ = Logger
            .AddScoped("InvitedBy", invitedBy)
            .BeginScope();

        try
        {
            _logger.LogInformation("Sending ChooseSticker action to test whether the bot has access to the chat.");

            // Test that bot is a member of the chat by trying to send an event.
            await _botClient.SendChatActionAsync(_chat.ChatId, ChatAction.ChooseSticker);

            _logger.LogInformation("Successfully sent ChooseSticker action. It means the bot has access to the chat.");

            // Do not send status messages if bot has already been in the chat.
            if (invitedBy != null)
            {
                _logger.LogInformation("The bot has been invited by a person. Sending a welcome message.");

                if (_config.Stickers.Any())
                {
                    var sticker = _config.Stickers.ElementAt(_random.Next(0, _config.Stickers.Count));
                    await _botClient.SendStickerAsync(_chat.ChatId, InputFile.FromFileId(sticker));

                    _logger.LogInformation("Sent welcome sticker {StickerId}.", sticker);
                }

                var directive = $"{_config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy}, tell them hello in your manner or thank the person for adding you if you feel like it.";
                var welcome = await _aiClient.GetCustomResponseAsync(directive);
                await _botClient.SendTextMessageAsync(_chat.ChatId, welcome);

                _logger.LogInformation("Sent welcome message '{Message}' (based on directive {Directive}).", welcome, directive);
            }

            _chat.MessageReceived += OnMessageReceived;
            _subscribed = true;
            _logger.LogInformation("Successfully joined the chat and subscribed to MessageReceived event.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to join bot to chat. This bot probably doesn't have access to the chat.");
            _chat.MessageReceived -= OnMessageReceived;
            _subscribed = false;
        }
    }

    private void OnStatusChanged(object sender, FoulStatusChanged message) => OnStatusChangedAsync(message);
    public async Task OnStatusChangedAsync(FoulStatusChanged message)
    {
        using var _ = Logger.BeginScope();
        _logger.LogDebug("Received StatusChanged message: {Message}", message);

        if (message.WhoName != _config.BotId)
        {
            _logger.LogDebug("StatusChanged message doesn't concern current bot, it's for {BotId} bot, skipping.", message.WhoName);
            return;
        }

        if (message.Status == ChatMemberStatus.Left
            || message.Status == ChatMemberStatus.Kicked)
        {
            _logger.LogDebug("The bot has been kicked from the group. Unsubscribing from updates.");
            _chat.MessageReceived -= OnMessageReceived;
            _subscribed = false;
            return;
        }

        // For any other status.

        if (_subscribed)
        {
            _logger.LogDebug("The bot status has changed but its already subscribed. Skipping.");
            return;
        }

        _logger.LogDebug("The bot has been invited to the group. Joining the chat. Invite was provided by {InvitedBy}.", message.ByName);
        await JoinChatAsync(message.ByName);
    }

    private void OnMessageReceived(object sender, FoulMessage message) => OnMessageReceivedAsync(message);
    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        using var _ = Logger.BeginScope();

        _logger.LogInformation("Handling received message: {@Message}.", message);

        if (_config.ReplyEveryMessages > 0)
        {
            _replyEveryMessagesCounter++;
            _logger.LogDebug("Incrementing mandatory message every {N} messages, new value: {Counter}.", _randomReplyEveryMessages, _replyEveryMessagesCounter);
        }

        async ValueTask<List<FoulMessage>?> ProcessSnapshotAsync()
        {
            _logger.LogTrace("Getting current context snapshot.");
            var snapshot = _chat.GetContextSnapshot();

            // Do not allow sending multiple messages. Just skip it till the next one arrives.
            if (snapshot.LastOrDefault() != null && snapshot[^1].SenderName == _config.BotId)
            {
                _logger.LogInformation("The last message in context is from the same bot. Skipping replying to it.");
                return null;
            }

            var unprocessedMessages = snapshot;

            if (_lastProcessedId != null)
            {
                unprocessedMessages = unprocessedMessages
                    .SkipWhile(message => message.Id != _lastProcessedId)
                    .Skip(1)
                    .ToList();

                _logger.LogDebug("{LastProcessedId} is not null, checking only unprocessed messages. There are {Count} of them.", _lastProcessedId, unprocessedMessages.Count);
            }

            if (!unprocessedMessages.Any())
            {
                _logger.LogDebug("There are no unprocessed messages. Skipping replying.");
                return null;
            }

            if (!unprocessedMessages.Exists(ShouldRespond) && _replyEveryMessagesCounter < _randomReplyEveryMessages && message.Id != "ByTime")
            {
                _logger.LogDebug("There are no messages that need processing (no keywords, no replies, no counters). Skipping replying.");
                return null;
            }

            if (unprocessedMessages[^1].IsOriginallyBotMessage && _botOnlyCount >= _botOnlyMaxCount)
            {
                _logger.LogInformation("Last message is from a bot. Exceeded bot-to-bot messages {Count}. Waiting for {Seconds} seconds for counter to decrease by 1. Skipping replying.", _botOnlyMaxCount, _config.BotOnlyDecrementIntervalSeconds);
                return null;
            }

            return snapshot;
        }

        if ((await ProcessSnapshotAsync()) == null)
            return;

        // We are only going inside the lock when we're sure we've got a message that needs reply from this bot.
        _logger.LogDebug("Acquiring lock for replying to the message.");
        await _lock.WaitAsync();
        try
        {
            using var l = Logger.AddScoped("Lock", Guid.NewGuid()).BeginScope();

            _logger.LogDebug("Acquired lock for replying to the message.");

            if ((await ProcessSnapshotAsync()) == null)
                return;

            _logger.LogDebug("Checked the context, it does contain messages that need a reply from the bot.");

            // Delay to "read" the messages.
            var delay = _random.Next(1, 100);
            if (delay > 90)
            {
                delay = _random.Next(5000, 20000);
            }
            if (delay <= 90 && delay > 70)
            {
                delay = _random.Next(1500, 5000);
            }
            if (delay <= 70)
            {
                delay = _random.Next(200, 1200);
            }

            _logger.LogDebug("Initiating artificial delay of {Delay} milliseconds to read the message with 'Bot's eyes'.", delay);
            await Task.Delay(delay);

            var snapshot = await ProcessSnapshotAsync();
            if (snapshot == null)
            {
                _logger.LogDebug("Rechecked the context, it doesn't contain valid messages for processing anymore. Skipping.");
                return;
            }

            _logger.LogDebug("Rechecked the context, messages need to be processed. Snapshot {@Context}.", snapshot.TakeLast(10));

            if (snapshot[^1].IsOriginallyBotMessage)
            {
                _botOnlyCount++;
                _logger.LogDebug("Last message is from a bot. Increasing bot-to-bot count. New value is {Count}.", _botOnlyCount);
            }

            // TODO: Consider doing that at the very end.
            _logger.LogDebug("Updating last processed ID: {PreviousLastProcessed} to {NewLastProcessed}.", _lastProcessedId, snapshot[^1].Id);
            _lastProcessedId = snapshot[^1].Id;

            var isAudio = false;
            if (_config.MessagesBetweenVoice > 0)
            {
                _audioCounter++;
                _logger.LogTrace("Messages between voice is configured, increasing the count to {Count}", _audioCounter);
            }

            if (_audioCounter > _config.MessagesBetweenVoice || _config.UseOnlyVoice)
            {
                _logger.LogTrace("Audio counter {Counter} exceeded configured value, or UseOnlyVoice is set. Replying with voice and resetting the counter.", _audioCounter);
                isAudio = true;
                _audioCounter = 0;
            }

            _logger.LogTrace("Initiating Typing or Voice sending imitator.");
            using var typing = new TypingImitator(_botClient, _chat.ChatId, isAudio ? ChatAction.RecordVoice : ChatAction.Typing);

            // Get context for this bot for the AI client.
            var context = GetContextForAI(snapshot);

            _logger.LogDebug("Context for AI: {@Context}", context);

            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

            if (_config.NotAnAssistant)
            {
                var i = 0;
                while (_failedContext.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                {
                    if (_failedContextCancellation.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                    {
                        _logger.LogWarning("Generated broken context message: {Message}, but it's actually OK. Continuing.", aiGeneratedTextResponse);
                        break;
                    }

                    _logger.LogWarning("Generated broken context message: {Message}. Trying to re-generate.", aiGeneratedTextResponse);


                    i++;
                    await Task.Delay(_random.Next(1100, 2300));
                    aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

                    if (i > 5)
                        throw new InvalidOperationException("Could not generate a valid response.");
                }
            }

            _logger.LogInformation("Generated context and response: {@Context}, {Response}.", context, aiGeneratedTextResponse);
            if (!_config.UseOnlyVoice)
            {
                if (isAudio)
                {
                    _logger.LogDebug("Sending audio instead of text based on audio rules.");
                    using var stream = await _aiClient.GetAudioResponseAsync(aiGeneratedTextResponse);

                    _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
                }
                else
                {
                    _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botClient.SendTextMessageAsync(_chat.ChatId, aiGeneratedTextResponse);
                }
            }
            else
            {
                _logger.LogDebug("Configured to always send Voice. Using Google TTS.");
                using var stream = await _googleTts.GetAudioAsync(aiGeneratedTextResponse);

                _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                await typing.FinishTypingText(aiGeneratedTextResponse);
                await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
            }

            _logger.LogDebug("Successfully replied to Telegram. Sending the Response as the next message to context.");

            // This will also cause this method to trigger again. Handle this on this level.
            // Adding this message to the global context ONLY after it has been received by telegram.
            _chat.AddMessage(new FoulMessage(Guid.NewGuid().ToString(), FoulMessageType.Bot, _config.BotName, aiGeneratedTextResponse, DateTime.UtcNow, true));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while processing the message.");
        }
        finally
        {
            _logger.LogDebug("Waiting for between 1 and 8 seconds after replying, before the next handle cycle starts.");

            // Wait at least 1 second after each reply, but random up to 8.
            await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(1000, 8000)));

            _logger.LogDebug("Releasing the message handling lock.");
            _lock.Release();
        }
    }

    private List<FoulMessage> GetContextForAI(List<FoulMessage> fullContext)
    {
        var onlyAddressedToMe = fullContext
            .Where(message => ShouldRespond(message) || IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_config.ContextSize)
            .ToList();

        var allMessages = fullContext
            .Where(message => !ShouldRespond(message) && !IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_config.ContextSize / 2) // Populate only second half with these messages.
            .ToList();

        var combinedContext = onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_config.ContextSize)
            .ToList();

        return new[] { new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false) }
            .Concat(combinedContext)
            .ToList();
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _config.BotName;
    }

    private bool ShouldRespond(FoulMessage message)
    {
        if (message.SenderName == _config.BotName)
            return false;

        if (message.ReplyTo == _config.BotId)
            return true;

        var triggerKeyword = _config.KeyWords.FirstOrDefault(k => message.Text.ToLowerInvariant().Contains(k.ToLowerInvariant().Trim()));
        if (triggerKeyword != null)
            return true;

        return false;
    }
}
