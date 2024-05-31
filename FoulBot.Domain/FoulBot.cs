using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FoulBot.Domain;

public sealed record Reminder(
    DateTime AtUtc,
    string Request,
    bool EveryDay)
{
    public string? Id { get; set; }
}

public sealed class ReminderCreator
{
    private readonly FoulChatId _chatId;
    private readonly string _botId;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private Dictionary<string, Reminder> _reminders = new Dictionary<string, Reminder>();

    public ReminderCreator(
        FoulChatId chatId,
        string botId)
    {
        _chatId = chatId;
        _botId = botId;

        // Load everything.
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (!Directory.Exists("reminders"))
                    Directory.CreateDirectory("reminders");

                if (!File.Exists($"reminders/{chatId}---{_botId}"))
                    File.WriteAllText($"reminders/{chatId}---{botId}", "[]");

                var content = File.ReadAllText($"reminders/{_chatId}---{_botId}");
                _reminders = JsonSerializer.Deserialize<IEnumerable<Reminder>>(content)!
                    .ToDictionary(x => x.Id!);

                foreach (var reminder in _reminders.Values)
                {
                    InitializeReminder(reminder.Id!);
                }
            }
        });
    }

    public EventHandler<string>? Remind;

    public void AddReminder(Reminder reminder)
    {
        lock (_lock)
        {
            var id = Guid.NewGuid().ToString();
            reminder.Id = id;
            _reminders.Add(id, reminder);
            SaveRemindersAsync().GetAwaiter().GetResult();
            InitializeReminder(id);
        }
    }

    public void AddReminder(string message)
    {
        var everyDay = false;
        if (message.StartsWith("каждый день"))
        {
            everyDay = true;
            message = message.Replace("каждый день", string.Empty);
        }

        var command = message.Replace("через", string.Empty).Trim();
        var number = Convert.ToInt32(command.Split(' ')[0]);
        var units = command.Split(' ')[1];
        var request = string.Join(' ', command.Split(' ').Skip(2));

        var time = DateTime.UtcNow;
        if (units.StartsWith("сек"))
            time = DateTime.UtcNow + TimeSpan.FromSeconds(number);
        if (units.StartsWith("мин"))
            time = DateTime.UtcNow + TimeSpan.FromMinutes(number);
        if (units.StartsWith("час"))
            time = DateTime.UtcNow + TimeSpan.FromHours(number);
        if (units.StartsWith("де") || units.StartsWith("дн"))
            time = DateTime.UtcNow + TimeSpan.FromDays(number);

        AddReminder(new Reminder(time, request, everyDay));
    }

    private void InitializeReminder(string id)
    {
        // Very simple implementation.
        Task.Run(async () =>
        {
            var reminder = _reminders[id];
            if (reminder.AtUtc > DateTime.UtcNow)
                await Task.Delay(reminder.AtUtc - DateTime.UtcNow);

            _reminders.Remove(id);
            if (reminder.EveryDay)
            {
                _reminders.Add(id, reminder with
                {
                    AtUtc = reminder.AtUtc + TimeSpan.FromDays(1)
                });
            }
            await SaveRemindersAsync();

            TriggerReminder(reminder);
        });
    }

    private void TriggerReminder(Reminder reminder)
    {
        Task.Run(() =>
        {
            Remind?.Invoke(this, reminder.Request);
        });
    }

    private async ValueTask SaveRemindersAsync()
    {
        await File.WriteAllTextAsync($"reminders/{_chatId}---{_botId}", JsonSerializer.Serialize(_reminders.Values));
    }
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
    public int ContextSize { get; init; } = 14;
    public int ReplyEveryMessages { get; } = 20;
    public int MessagesBetweenVoice { get; init; } = 0;
    public bool UseOnlyVoice { get; init; } = false;
    public int BotOnlyMaxMessagesBetweenDebounce { get; init; } = 3;
    public int BotOnlyDecrementIntervalSeconds { get; init; } = 130;
    public bool NotAnAssistant { get; init; } = true;
    public HashSet<string> Stickers { get; } = new HashSet<string>();

    public FoulBotConfiguration SetContextSize(int contextSize)
    {
        return this with
        {
            ContextSize = contextSize
        };
    }

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
    ValueTask JoinChatAsync(string? invitedBy);
}

public sealed class FoulBot : IFoulBot
{
    private static readonly string[] _failedContext = [
        "извините", "sorry", "простите", "не могу продолжать",
        "не могу участвовать", "давайте воздержимся", "прошу прощения",
        "прошу вас выражаться", "извини", "прости", "не могу помочь",
        "не могу обсуждать", "мои извинения", "нужно быть доброжелательным",
        "не думаю, что это хороший тон", "уважительным по отношению к другим",
        "поведения не терплю",
        "stop it right there", "I'm here to help", "with any questions",
        "can assist", "не будем оскорблять", "не буду оскорблять", "не могу оскорблять",
        "нецензурн", "готов помочь", "с любыми вопросами", "за грубость",
        "не буду продолжать", "призываю вас к уважению", "уважению друг к другу"
    ];
    private static readonly string[] _failedContextCancellation = [
        "ссылок", "ссылк", "просматривать ссыл", "просматривать содерж",
        "просматривать контент", "прости, но"
    ];
    private readonly ILogger<FoulBot> _logger;
    private readonly IFoulAIClient _aiClient;
    private readonly IBotMessenger _botMessenger;
    private readonly FoulBotConfiguration _config;
    private readonly IGoogleTtsService _googleTtsService;
    private readonly ITypingImitatorFactory _typingImitatorFactory;
    private readonly IFoulChat _chat;
    private readonly Random _random = new Random();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly ReminderCreator _reminderCreator;
    private readonly Task _botOnlyDecrementTask;
    private readonly Task _repeatByTimeTask;

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
        IGoogleTtsService googleTtsService,
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        ITypingImitatorFactory typingImitatorFactory,
        IFoulChat chat)
    {
        _logger = logger;
        _aiClient = aiClient;
        _googleTtsService = googleTtsService;
        _botMessenger = botMessenger;
        _config = configuration;
        _typingImitatorFactory = typingImitatorFactory;
        _chat = chat;
        _chat.StatusChanged += OnStatusChanged;

        _reminderCreator = new ReminderCreator(
            _chat.ChatId, _config.BotId);
        _reminderCreator.Remind += OnRemind;

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

                try
                {
                    await OnMessageReceivedAsync(FoulMessage.ByTime());
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error when trying to trigger reply by time passed.");
                }
            }
        });

        /*
        var celebrate = Task.Run(async () =>
        {
            var current = DateTime.UtcNow;
            var release = new DateTime(2024, 5, 22, 10, 0, 0, DateTimeKind.Utc);
            if (release < current)
                return;

            var time = release - current;
            await Task.Delay(time);

            var response = await _aiClient.GetCustomResponseAsync($"{_config.Directive}. You are a bot and you have finally been released as version 1.0. Tell people about it so that they should celebrate.");

            if (_config.Stickers.Count > 0)
            {
                var sticker = _config.Stickers.ElementAt(_random.Next(0, _config.Stickers.Count));
                await _botClient.SendStickerAsync(_chat.ChatId, InputFile.FromFileId(sticker));
            }
            await _botClient.SendTextMessageAsync(_chat.ChatId, response);
        });
        */
    }

    private void OnRemind(object? sender, string request) => RemindAsync(request);

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
            if (!await _botMessenger.CheckCanWriteAsync(_chat.ChatId))
            {
                _logger.LogWarning("Failed to join bot to chat. This bot probably doesn't have access to the chat.");
                _chat.MessageReceived -= OnMessageReceived;
                _subscribed = false;
                return;
            }

            _logger.LogInformation("Successfully sent ChooseSticker action. It means the bot has access to the chat.");

            // Do not send status messages if bot has already been in the chat.
            if (invitedBy != null)
            {
                _logger.LogInformation("The bot has been invited by a person. Sending a welcome message.");

                if (_config.Stickers.Any())
                {
                    var sticker = _config.Stickers.ElementAt(_random.Next(0, _config.Stickers.Count));
                    await _botMessenger.SendStickerAsync(_chat.ChatId, sticker);

                    _logger.LogInformation("Sent welcome sticker {StickerId}.", sticker);
                }

                var directive = $"{_config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy}, tell them hello in your manner or thank the person for adding you if you feel like it.";
                var welcome = await _aiClient.GetCustomResponseAsync(directive);
                await _botMessenger.SendTextMessageAsync(_chat.ChatId, welcome);

                _logger.LogInformation("Sent welcome message '{Message}' (based on directive {Directive}).", welcome, directive);
            }

            _chat.MessageReceived += OnMessageReceived;
            _subscribed = true;
            _logger.LogInformation("Successfully joined the chat and subscribed to MessageReceived event.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send welcome message to chat.");
        }
    }

    private void OnStatusChanged(object? sender, FoulStatusChanged message)
    {
        Task.Run(async () =>
        {
            try
            {
                await OnStatusChangedAsync(message);
            }
            catch (Exception exception)
            {
                using var _ = Logger
                    .AddScoped("Message", message)
                    .BeginScope();

                _logger.LogError(exception, "Error when handling the status changed message.");
            }
        });
    }


    public async Task OnStatusChangedAsync(FoulStatusChanged message)
    {
        using var _ = Logger.BeginScope();
        _logger.LogDebug("Received StatusChanged message: {Message}", message);

        if (message.WhoName != _config.BotId)
        {
            _logger.LogDebug("StatusChanged message doesn't concern current bot, it's for {BotId} bot, skipping.", message.WhoName);
            return;
        }

        if (message.Status == BotChatStatus.Left)
        {
            _logger.LogDebug("The bot has been kicked from the group. Unsubscribing from updates.");
            _chat.MessageReceived -= OnMessageReceived;
            _subscribed = false;
            return;
        }

        if (message.Status != BotChatStatus.Joined)
            throw new InvalidOperationException("Unknown status.");

        // For Joined status.
        if (_subscribed)
        {
            _logger.LogDebug("The bot status has changed but its already subscribed. Skipping.");
            return;
        }

        _logger.LogDebug("The bot has been invited to the group. Joining the chat. Invite was provided by {InvitedBy}.", message.ByName);
        await JoinChatAsync(message.ByName);
    }

    private bool _shutup;
    private void OnMessageReceived(object? sender, FoulMessage message)
    {
        if (message.Text == "$shutup")
        {
            _shutup = true;
            return;
        }

        if (message.Text == "$unshutup")
        {
            _shutup = false;
            return;
        }

        if (_shutup)
        {
            _logger.LogDebug("Shutup command has been issued. Skipping all communication.");
            return;
        }

        if (message.Text.StartsWith($"@{_config.BotId}"))
        {
            _reminderCreator.AddReminder(message.Text.Replace($"@{_config.BotId}", string.Empty).Trim());
            _logger.LogDebug("Reminder command has been issued. Setting up a reminder.");
            // Test code.
            try
            {
                var command = message.Text.Replace(_config.BotId, string.Empty).Trim().Replace("напомни через", string.Empty).Trim();
                var number = Convert.ToInt32(command.Split(' ')[0]);
                var units = command.Split(' ')[1];

                Task.Run(async () =>
                {
                    if (units.StartsWith("сек"))
                        await Task.Delay(TimeSpan.FromSeconds(number));
                    if (units.StartsWith("мин"))
                        await Task.Delay(TimeSpan.FromMinutes(number));
                    if (units.StartsWith("час"))
                        await Task.Delay(TimeSpan.FromHours(number));
                    if (units.StartsWith("де") || units.StartsWith("дн"))
                        await Task.Delay(TimeSpan.FromDays(number));

                    await RemindAsync(string.Join(' ', command.Split(' ').Skip(2)));
                });
            }
            catch
            {
            }
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await OnMessageReceivedAsync(message);
            }
            catch (Exception exception)
            {
                using var _ = Logger
                    .AddScoped("Message", message)
                    .BeginScope();

                _logger.LogError(exception, "Error when handling the message.");
            }
        });
    }

    private async Task RemindAsync(string message)
    {
        var response = await _aiClient.GetCustomResponseAsync(_config.Directive + $" Ты должен сделать следующее: {message}");

        await _botMessenger.SendTextMessageAsync(_chat.ChatId, response);
    }

    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        using var _ = Logger.BeginScope();

        // This method is called on a timer too. We need to skip it if the bot is not in chat.
        if (!_subscribed)
        {
            _logger.LogDebug("Message received method has been triggered (probably by time) but the bot is not subscribed (not in chat). Skipping.");
            return;
        }

        _logger.LogInformation("Handling received message: {@Message}.", message);

        if (_config.ReplyEveryMessages > 0)
        {
            _replyEveryMessagesCounter++;
            _logger.LogDebug("Incrementing mandatory message every {N} messages, new value: {Counter}.", _randomReplyEveryMessages, _replyEveryMessagesCounter);
        }

        // TODO: Consider the situation: You write A, bot starts to type, you write B while bot is typing, bot writes C.
        // The context is as follows: You:A, You:B, Bot:C. But bot did not respond to your B message, he replied to your A message.
        // Currently he will NOT proceed to reply to your B message because the last message is HIS one.
        async ValueTask<List<FoulMessage>?> ProcessSnapshotAsync()
        {
            _logger.LogTrace("Getting current context snapshot.");
            var snapshot = _chat.GetContextSnapshot();

            // Do not allow sending multiple messages. Just skip it till the next one arrives.
            if (snapshot.LastOrDefault() != null && snapshot[^1].SenderName == _config.BotName)
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

            if (unprocessedMessages[^1].IsOriginallyBotMessage && _botOnlyCount >= _config.BotOnlyMaxMessagesBetweenDebounce)
            {
                _logger.LogInformation("Last message is from a bot. Exceeded bot-to-bot messages {Count}. Waiting for {Seconds} seconds for counter to decrease by 1. Skipping replying.", _config.BotOnlyMaxMessagesBetweenDebounce, _config.BotOnlyDecrementIntervalSeconds);
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

            // TODO: This is partially duplicated code from the GetSnapshot method, just for the sake of logging.
            string? reason = null;
            {
                var unprocessedMessages = snapshot;

                if (_lastProcessedId != null)
                {
                    unprocessedMessages = unprocessedMessages
                        .SkipWhile(message => message.Id != _lastProcessedId)
                        .Skip(1)
                        .ToList();
                }

                reason = unprocessedMessages
                    .Select(m => GetReasonForResponding(m))
                    .Where(r => r != null)
                    .FirstOrDefault();

                _logger.LogInformation("Reason (trigger) for replying: {Reason}", reason);
            }

            // If we get here - we are *going* to send the reply. We can reset counters if necessary.
            if (_replyEveryMessagesCounter >= _randomReplyEveryMessages)
                SetupNextReplyEveryMessages();

            _logger.LogDebug("Rechecked the context, messages need to be processed. Snapshot {@Context}.", snapshot.TakeLast(10));

            if (snapshot[^1].IsOriginallyBotMessage)
            {
                _botOnlyCount++;
                _logger.LogDebug("Last message is from a bot. Increasing bot-to-bot count. New value is {Count}.", _botOnlyCount);
            }

            // TODO: Consider doing that at the very end.
            _logger.LogDebug("Updating last processed ID: {PreviousLastProcessed} to {NewLastProcessed}.", _lastProcessedId, snapshot[^1].Id);
            _lastProcessedId = snapshot[^1].Id;

            var isVoice = false;
            if (_config.MessagesBetweenVoice > 0)
            {
                _audioCounter++;
                _logger.LogTrace("Messages between voice is configured, increasing the count to {Count}", _audioCounter);
            }

            if (_audioCounter > _config.MessagesBetweenVoice || _config.UseOnlyVoice)
            {
                _logger.LogTrace("Audio counter {Counter} exceeded configured value, or UseOnlyVoice is set. Replying with voice and resetting the counter.", _audioCounter);
                isVoice = true;
                _audioCounter = 0;
            }

            _logger.LogTrace("Initiating Typing or Voice sending imitator.");
            await using var typing = _typingImitatorFactory.ImitateTyping(_chat.ChatId, isVoice);

            // Get context for this bot for the AI client.
            var context = GetContextForAI(snapshot);

            _logger.LogDebug("Context for AI: {@Context}", context);

            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

            var isBadResponse = false;
            if (_config.NotAnAssistant)
            {
                var i = 1;
                while (_failedContext.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                {
                    if (_failedContextCancellation.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
                    {
                        _logger.LogWarning("Generated broken context message: {Message}, but it's actually OK. Continuing.", aiGeneratedTextResponse);
                        break;
                    }

                    // When we generated 3 responses already, and they are all bad.
                    if (i >= 3)
                    {
                        // Used in order not to add this message to the context. It will spoil the context for future requests.
                        isBadResponse = true;
                        _logger.LogWarning("Generated broken context message: {Message}. NOT adding it to context, but sending it to the user cause it was the last attempt.", aiGeneratedTextResponse);
                        break;
                    }

                    _logger.LogWarning("Generated broken context message: {Message}. Repeating main directive and trying to re-generate.", aiGeneratedTextResponse);

                    i++;
                    await Task.Delay(_random.Next(1100, 2300));
                    context.Add(new FoulMessage(
                        "Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false));

                    aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);
                }
            }

            _logger.LogInformation("Context, reason and response: {@Context}, {Reason}, {Response}.", context, reason, aiGeneratedTextResponse);
            if (!_config.UseOnlyVoice)
            {
                if (isVoice)
                {
                    _logger.LogDebug("Sending audio instead of text based on audio rules.");
                    using var stream = await _aiClient.GetAudioResponseAsync(aiGeneratedTextResponse);

                    _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botMessenger.SendVoiceMessageAsync(_chat.ChatId, stream);
                }
                else
                {
                    _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botMessenger.SendTextMessageAsync(_chat.ChatId, aiGeneratedTextResponse);
                }
            }
            else
            {
                _logger.LogDebug("Configured to always send Voice. Using Google TTS.");
                using var stream = await _googleTtsService.GetAudioAsync(aiGeneratedTextResponse);

                _logger.LogDebug("Finishing typing simulation just before sending reply to Telegram.");
                await typing.FinishTypingText(aiGeneratedTextResponse);
                await _botMessenger.SendVoiceMessageAsync(_chat.ChatId, stream);
            }

            _logger.LogDebug("Successfully replied to Telegram. Sending the Response as the next message to context.");

            if (isBadResponse)
                return; // Do not add this message to the context / process it, because it will spoil the context for all the subsequent requests.

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
        return GetReasonForResponding(message) != null;
    }

    private string? GetReasonForResponding(FoulMessage message)
    {
        if (message.SenderName == _config.BotName)
            return null;

        if (message.ReplyTo == _config.BotId)
            return "Reply";

        if (_chat.IsPrivateChat)
            return "Private chat"; // Reply to all messages in a private chat.

        var triggerKeyword = _config.KeyWords.FirstOrDefault(k => message.Text.ToLowerInvariant().Contains(k.ToLowerInvariant().Trim()));
        if (triggerKeyword != null)
            return $"Trigger word: '{triggerKeyword}'";

        return null;
    }
}
