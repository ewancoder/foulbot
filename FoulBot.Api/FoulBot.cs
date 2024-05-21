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

public interface IFoulBot
{
    string BotId { get; }
    ValueTask<bool> JoinChatAsync(IFoulChat chat, string invitedBy = null);
}

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
    private readonly ILogger<FoulBot> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IFoulAIClient _aiClient;
    private readonly GoogleTtsService _googleTts = new GoogleTtsService();
    private readonly Random _random = new Random();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly Task _botOnlyDecrementTask;
    private readonly Task _repeatByTimeTask;

    private readonly string _botIdName;
    private readonly string _botName;
    private readonly HashSet<string> _keyWords;
    private readonly string _directive;
    private readonly int _contextSize;
    private readonly int _replyEveryMessages;
    private readonly int _messagesBetweenAudio;
    private readonly bool _useOnlyVoice;
    private readonly int _botOnlyMaxCount = 10;
    private readonly int _botOnlyDecrementIntervalSeconds = 60;
    private readonly bool _notAnAssistant;
    private readonly List<string> _stickers;

    private bool _subscribed;
    private bool _subscribedToStatusChanged = false;
    private int _botOnlyCount = 0;
    private int _audioCounter = 0;
    private int _randomReplyEveryMessages;
    private int _replyEveryMessagesCounter = 0;
    private IFoulChat _chat;
    private string? _lastProcessedId;

    public FoulBot(
        ILogger<FoulBot> logger,
        IFoulAIClient aiClient,
        ITelegramBotClient botClient,
        FoulBotConfiguration configuration)
    {
        _logger = logger;
        _stickers = configuration.Stickers.ToList();
        _aiClient = aiClient;
        _botClient = botClient;
        _directive = configuration.Directive;
        _botIdName = configuration.BotId;
        _botName = configuration.BotName;
        _keyWords = configuration.KeyWords;
        _contextSize = configuration.ContextSize;
        _replyEveryMessages = configuration.ReplyEveryMessages;
        _randomReplyEveryMessages = _random.Next(Math.Max(1, _replyEveryMessages - 5), _replyEveryMessages + 15);
        _messagesBetweenAudio = configuration.MessagesBetweenVoice;
        _useOnlyVoice = configuration.UseOnlyVoice;
        _notAnAssistant = configuration.NotAnAssistant;
        _botOnlyDecrementTask = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_botOnlyDecrementIntervalSeconds));
                if (_botOnlyCount > 0)
                    _botOnlyCount--;
            }
        });
        _repeatByTimeTask = Task.Run(async () =>
        {
            while (true)
            {
                var waitMinutes = _random.Next(60, 720);
                if (waitMinutes < 120)
                    waitMinutes = _random.Next(60, 720);

                _logger.LogInformation("Prepairing to wait for a message based on time, {waitMinutes} minutes. Bot {bot}, chat {chat}.", waitMinutes, _botIdName, _chat?.ChatId);
                await Task.Delay(TimeSpan.FromMinutes(waitMinutes));
                _logger.LogInformation("Sending a message based on time, {waitMinutes} minutes passed. Bot {bot}, chat {chat}.", waitMinutes, _botIdName, _chat?.ChatId);
                await OnMessageReceivedAsync(FoulMessage.ByTime());
            }
        });
    }

    public string BotId => _botIdName;

    // TODO: If after restarting the solution someone REMOVES a bot from the chat - all the other bots will comment on it as if you have ADDED them.
    // This is because the chat object has not been created yet and this method is called for everyone.
    public async ValueTask<bool> JoinChatAsync(IFoulChat chat, string invitedBy = null)
    {
        try
        {
            _chat = chat;
            if (!_subscribedToStatusChanged)
            {
                _chat.StatusChanged += OnStatusChanged;
                _subscribedToStatusChanged = true;
            }

            // Test that bot is a member of the chat by trying to send an event.
            await _botClient.SendChatActionAsync(chat.ChatId, ChatAction.ChooseSticker);

            // Do not send status messages if bot has already been in the chat.
            if (invitedBy != null)
            {
                if (_stickers.Any())
                    await _botClient.SendStickerAsync(chat.ChatId, InputFile.FromFileId(_stickers[_random.Next(0, _stickers.Count)]));

                var welcome = await _aiClient.GetCustomResponseAsync(
$"{_directive}. You have just been added to a chat group with a number of people by a person named {invitedBy}, tell them hello in your manner or thank the person for adding you if you feel like it.");
                await _botClient.SendTextMessageAsync(chat.ChatId, welcome);
            }

            _logger.LogInformation("Bot {botName} joined chat {chatId}.", _botName, chat.ChatId);
            _chat.MessageReceived += OnMessageReceived;
            _subscribed = true;
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to join {botClient} to chat {chatId}.", _botName, chat.ChatId);
            return false;
        }
    }

    private void OnMessageReceived(object sender, FoulMessage message) => OnMessageReceivedAsync(message);
    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        _logger.LogDebug("Message received by bot {botName} in chat {chatId}, message {@message}.", _botName, _chat.ChatId, message);

        if (_replyEveryMessages > 0)
            _replyEveryMessagesCounter++;

        // TODO: Do NOT send multiple messages in a row if you just sent some. Otherwise it's possible that bot will always talk with itself.
        // BUT if there are MULTIPLE new messages in context apart from yours AND they match your keywords - DO send once more.

        // TODO: Consider not processing the accumulated messages after lock has been released.

        // We don't care about getting it in order - it's getting added there in order, but we just grab the current state at the moment when we receive the message.
        // We can't place it inside the lock or everything will hang because EVERY message will be waiting
        var snapshot = _chat.GetContextSnapshot();

        // Do not allow sending multiple messages. Just skip it till the next one arrives.
        if (snapshot.LastOrDefault() != null && snapshot[^1].SenderName == _botIdName)
            return;

        var unprocessedMessages = snapshot;

        if (_lastProcessedId != null)
        {
            unprocessedMessages = unprocessedMessages
                .SkipWhile(message => message.Id != _lastProcessedId)
                .Skip(1)
                .ToList();
        }

        if (!unprocessedMessages.Any())
        {
            LogDebug("No unprocessed messages. Returning.");
            return;
        }

        if (!unprocessedMessages.Exists(ShouldAct) && _replyEveryMessagesCounter < _randomReplyEveryMessages && message.Id != "ByTime")
        {
            LogDebug("Exiting because there are no messages that need to be processed.");
            return;
        }

        if (unprocessedMessages[^1].IsOriginallyBotMessage)
        {
            if (_botOnlyCount >= _botOnlyMaxCount)
            {
                LogDebug("Exceeded bot-to-bot messages count. Waiting for {botOnlyDecrementIntervalSeconds} seconds to decrease the count. Meanwhile all messages are lost.", _botOnlyDecrementIntervalSeconds);
                return;
            }

            LogDebug("Increasing bot-only count.");
            _botOnlyCount++;
        }

        // We are only going inside the lock when we're sure we've got a message that needs reply from this bot.
        LogDebug("Acquiring lock.");
        await _lock.WaitAsync();
        try
        {
            LogDebug("Inside lock.");
            {
                // After waiting - let's grab context one more time.
                snapshot = _chat.GetContextSnapshot();

                // Do not allow sending multiple messages. Just skip it till the next one arrives.
                if (snapshot.LastOrDefault() != null && snapshot[^1].SenderName == _botIdName)
                    return;

                unprocessedMessages = snapshot;

                if (_lastProcessedId != null)
                {
                    unprocessedMessages = unprocessedMessages
                        .SkipWhile(message => message.Id != _lastProcessedId)
                        .Skip(1)
                        .ToList();
                }

                if (!unprocessedMessages.Exists(ShouldAct) && _replyEveryMessagesCounter < _randomReplyEveryMessages && message.Id != "ByTime")
                    return;

                if (_replyEveryMessagesCounter >= _randomReplyEveryMessages)
                {
                    // TODO: Add randomness here.
                    _replyEveryMessagesCounter = 0;
                    _randomReplyEveryMessages = _random.Next(Math.Max(1, _replyEveryMessages - 5), _replyEveryMessages + 15);
                    LogDebug("Reset counter for N messages repeat.");
                }

                if (unprocessedMessages[^1].IsOriginallyBotMessage)
                {
                    if (_botOnlyCount >= _botOnlyMaxCount)
                    {
                        LogDebug("Exceeded bot-to-bot messages count. Waiting for {botOnlyDecrementIntervalSeconds} seconds to decrease the count.", _botOnlyDecrementIntervalSeconds);
                        return;
                    }

                    _botOnlyCount++;
                }
            }

            // TODO: This is duplicated code because after we enter the lock we still need to do this (after the previous message got handled).

            /*while (unprocessedMessages[^1].SenderName == _botName)
                unprocessedMessages.RemoveAt(unprocessedMessages.Count - 1);*/

            // Delay to "read" the messages.
            {
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
                await Task.Delay(delay);
                snapshot = _chat.GetContextSnapshot();
            }

            // Consider doing that at the very end.
            _lastProcessedId = snapshot[^1].Id;

            var isAudio = false;
            if (_messagesBetweenAudio > 0)
                _audioCounter++;
            if (_audioCounter > _messagesBetweenAudio || _useOnlyVoice)
            {
                isAudio = true;
                _audioCounter = 0;
            }

            using var typing = new TypingImitator(_botClient, _chat.ChatId, isAudio ? ChatAction.RecordVoice : ChatAction.Typing);

            // TODO: Imitate typing.

            // Get context from this bot for the AI client.
            var context = GetContextForAI(snapshot);

            _logger.LogDebug("Bot {botName} in chat {chatId} is processing context {@context}", _botName, _chat.ChatId, context);

            /*var aiGeneratedTextResponse = $"{DateTime.UtcNow} - bot reply from {_botName}";*/
            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);
            var contextForLogging = string.Join('\n', context.Select(c => c.ToString()));

            if (_notAnAssistant)
            {
                var i = 0;
                while (_failedContext.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                {
                    _logger.LogWarning("Generated broken context message: {message} from bot {botName} in chat {chatId}, trying to regenerate.", aiGeneratedTextResponse, _botName, _chat.ChatId);

                    i++;
                    await Task.Delay(1000);
                    aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

                    if (i > 5)
                        throw new InvalidOperationException("Could not generate a valid response.");
                }
            }

            _logger.LogInformation("Generated (message, bot, chat): {message} {bot} {chat}", aiGeneratedTextResponse, _botName, _chat.ChatId);
            if (!_useOnlyVoice)
            {
                if (isAudio)
                {
                    using var stream = await _aiClient.GetAudioResponseAsync(aiGeneratedTextResponse);

                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
                }
                else
                {
                    await _botClient.SendTextMessageAsync(_chat.ChatId, aiGeneratedTextResponse);
                    LogDebug(contextForLogging);
                }
            }
            else
            {
                using var stream = await _googleTts.GetAudioAsync(aiGeneratedTextResponse);

                await typing.FinishTypingText(aiGeneratedTextResponse);
                await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
            }

            // This will also cause this method to trigger again. Handle this on this level.
            // Add this message to the global context ONLY after it has been received by telegram.
            _chat.AddMessage(new FoulMessage(Guid.NewGuid().ToString(), FoulMessageType.Bot, _botName, aiGeneratedTextResponse, DateTime.UtcNow, true));

            LogContextAndResponse(context, aiGeneratedTextResponse);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error while processing the message by {botName} in {chatId}.", _botName, _chat.ChatId);
        }
        finally
        {
            // Wait at least 3 seconds after each reply, but random up to 12.
            await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(1000, 8000)));

            _lock.Release();
            LogDebug("We got to finally.");
        }
    }

    private void OnStatusChanged(object sender, FoulStatusChanged message) => OnStatusChangedAsync(message);
    public async Task OnStatusChangedAsync(FoulStatusChanged message)
    {
        if (message.WhoName != _botIdName)
            return;

        if (message.Status == ChatMemberStatus.Left
            || message.Status == ChatMemberStatus.Kicked)
        {
            _chat.MessageReceived -= OnMessageReceived;
            _subscribed = false;
            return;
        }

        // For any other status.
        if (!_subscribed)
            await JoinChatAsync(_chat, message.ByName);
    }

    private void LogDebug(string message, params object[] objects)
    {
        var args = objects
            .Append(_botName)
            .Append(_chat.ChatId)
            .ToArray();

        _logger.LogDebug(message + " Bot {botName}, Chat {chatId}.", args);
    }

    private void LogContextAndResponse(List<FoulMessage> context, string aiGeneratedTextResponse)
    {
        LogDebug("Context and response {@context}, {response}", context, aiGeneratedTextResponse);
    }

    private List<FoulMessage> GetContextForAI(List<FoulMessage> fullContext)
    {
        var onlyAddressedToMe = fullContext
            .Where(message => ShouldAct(message) || IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_contextSize)
            .ToList();

        var allMessages = fullContext
            .Where(message => !ShouldAct(message) && !IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_contextSize / 2) // Populate only second half with these messages.
            .ToList();

        var combinedContext = onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_contextSize)
            .ToList();

        return new[] { new FoulMessage("Directive", FoulMessageType.System, "System", _directive, DateTime.MinValue, false) }
            .Concat(combinedContext)
            .ToList();
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _botName;
    }

    private bool ShouldAct(FoulMessage message)
    {
        // TODO: Determine whether the bot should act (by key words, or by time, etc).
        if (message.SenderName == _botName)
            return false;

        if (message.ReplyTo == _botIdName)
            return true;

        var triggerKeyword = _keyWords.FirstOrDefault(k => message.Text.ToLowerInvariant().Contains(k.ToLowerInvariant().Trim()));
        if (triggerKeyword != null)
        {
            _logger.LogDebug("Trigger word for the message: {triggerWord}, {message}", triggerKeyword, message.Text);
            return true;
        }

        return false;
    }
}
