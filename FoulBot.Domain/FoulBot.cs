using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FoulBot.Domain;

public interface IFoulBot
{
    string BotId { get; }
    string ChatId { get; }
    ValueTask JoinChatAsync(string? invitedBy);
    IEnumerable<Reminder> AllReminders { get; }
}

public sealed class FoulBot : IFoulBot
{
    private readonly ILogger<FoulBot> _logger;
    private readonly IFoulAIClient _aiClient;
    private readonly IBotMessenger _botMessenger;
    private readonly FoulBotConfiguration _config;
    private readonly IGoogleTtsService _googleTtsService;
    private readonly ITypingImitatorFactory _typingImitatorFactory;
    private readonly IFoulChat _chat;
    private readonly IMessageRespondStrategy _messageRespondStrategy;
    private readonly IContextReducer _contextReducer;
    private readonly IFoulBotContext _botContext;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly IContextPreserverClient _contextPreserverClient;
    private readonly Random _random = new Random();
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly ReminderCreator _reminderCreator;
    private readonly Task _botOnlyDecrementTask;
    private readonly Task _talkByTime;

    private int _randomReplyEveryMessages;
    private bool _subscribed = false;
    private int _botToBotCommunicationCounter = 0;
    private int _audioCounter = 0;
    private int _replyEveryMessagesCounter = 0;

    public FoulBot(
        ILogger<FoulBot> logger,
        IFoulAIClient aiClient,
        IGoogleTtsService googleTtsService,
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        ITypingImitatorFactory typingImitatorFactory,
        IFoulChat chat,
        IMessageRespondStrategy respondStrategy,
        IContextReducer contextReducer,
        IFoulBotContext botContext,
        IBotDelayStrategy delayStrategy,
        IContextPreserverClient contextPreserverClient)
    {
        _botContext = botContext;
        _logger = logger;
        _aiClient = aiClient;
        _googleTtsService = googleTtsService;
        _botMessenger = botMessenger;
        _config = configuration;
        _typingImitatorFactory = typingImitatorFactory;
        _chat = chat;
        _messageRespondStrategy = respondStrategy;
        _contextReducer = contextReducer;
        _delayStrategy = delayStrategy;
        _contextPreserverClient = contextPreserverClient;
        _reminderCreator = new ReminderCreator(
            _chat.ChatId, _config.BotId);

        _chat.StatusChanged += OnStatusChanged;
        _reminderCreator.Remind += OnRemind;

        using var _ = Logger.BeginScope();

        SetupNextReplyEveryMessages();

        _botOnlyDecrementTask = DecrementBotToBotCommunicationCounterAsync();
        _talkByTime = TalkByTimeAsync();
    }

    private async Task DecrementBotToBotCommunicationCounterAsync()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.DecrementBotToBotCommunicationCounterIntervalSeconds));

            if (_botToBotCommunicationCounter > 0)
            {
                _logger.LogTrace("Reduced bot-to-bot debounce to -1, current value {Value}", _botToBotCommunicationCounter);
                _botToBotCommunicationCounter--;
            }
        }
    }

    private async Task TalkByTimeAsync()
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
    }

    public string BotId => _config.BotId;
    public string ChatId => _chat.ChatId.ToString();
    public IEnumerable<Reminder> AllReminders => _reminderCreator.AllReminders;

    private void OnRemind(object? sender, Reminder reminder) => RemindAsync(reminder);

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
            _logger.LogDebug("Reminder command has been issued. Setting up a reminder: {Message}", message);
            _reminderCreator.AddReminder(message);
            // Do not return - add the ask to set up a reminder as a message to context.
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

    private async Task RemindAsync(Reminder reminder)
    {
        // TODO: Add shared code from OnMessageReceived method, like NOT processing this if bot is NOT added to the chat anymore (unsubscribed).
        // TODO: Log everything and log amount of tokens.

        var response = await _aiClient.GetCustomResponseAsync(_config.Directive + $" Ты должен сделать следующее ({reminder.From} попросил): {reminder.Request}");

        await _botMessenger.SendTextMessageAsync(_chat.ChatId, response);

        // Send currently generated message to context.
        // TODO: Make sure it is inserted in the right order.
        _chat.AddMessage(new FoulMessage(Guid.NewGuid().ToString(), FoulMessageType.Bot, _config.BotName, response, DateTime.UtcNow, true));
    }

    private void HandleAnyMessageReceived()
    {
        if (_config.ReplyEveryMessages > 0)
        {
            _replyEveryMessagesCounter++;
            _logger.LogDebug(
                "Incrementing mandatory message every {N} messages, new value: {Counter}.",
                _randomReplyEveryMessages, _replyEveryMessagesCounter);
        }
    }

    private void HandlePrepairingReply(List<FoulMessage> snapshot, out bool isVoice)
    {
        if (_replyEveryMessagesCounter >= _randomReplyEveryMessages)
            SetupNextReplyEveryMessages();

        if (snapshot[^1].IsOriginallyBotMessage)
        {
            _botToBotCommunicationCounter++;
            _logger.LogDebug("Last message is from a bot. Increasing bot-to-bot count. New value is {Count}.", _botToBotCommunicationCounter);
        }

        if (_config.MessagesBetweenVoice > 0)
        {
            _audioCounter++;
            _logger.LogTrace("Messages between voice is configured, increasing the count to {Count}", _audioCounter);
        }

        // If audio counter is due, or OnlyVoice is true - set isVoice to true.
        isVoice = _audioCounter > _config.MessagesBetweenVoice || _config.UseOnlyVoice;
        if (isVoice)
        {
            _logger.LogTrace("Audio counter {Counter} exceeded configured value, or UseOnlyVoice is set. Replying with voice and resetting the counter.", _audioCounter);
            _audioCounter = 0;
        }
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

        // Handle all counters and other logic when ANY message has been received,
        // even the one we don't need to reply to.
        _logger.LogDebug("Handling ANY message received.");
        HandleAnyMessageReceived();

        // This checks all the logic including counters, whether we should reply.
        if (GetSnapshotIfNeedToReply(message) == null)
            return;

        // We are only going inside the lock when we're sure we've got a message that needs reply from this bot.
        _logger.LogDebug("Acquiring lock for replying to the message.");
        await _lock.WaitAsync();
        try
        {
            using var l = Logger.AddScoped("Lock", Guid.NewGuid()).BeginScope();

            _logger.LogDebug("Acquired lock for replying to the message.");

            var snapshot = GetSnapshotIfNeedToReply(message);
            if (snapshot == null)
            {
                _logger.LogDebug("Rechecked the context, it doesn't contain valid messages for processing anymore. Skipping.");
                return;
            }

            // If we get here - we are *going* to send the reply. We can reset counters if necessary.
            _logger.LogDebug("Checked the context, it does contain messages that need a reply from the bot. Snapshot {@Context}.", snapshot.TakeLast(10));
            _logger.LogDebug("Handling prepairing reply.");
            HandlePrepairingReply(snapshot, out var isVoice);

            // Delay to simulate "reading" the messages by the bot.
            await _delayStrategy.DelayAsync();

            // Get the exact reason why we are replying to that message.
            var reason = snapshot
                .Select(_messageRespondStrategy.GetReasonForResponding)
                .FirstOrDefault(reason => reason != null);

            // If the reason is null - it means that no triggers were present in the messages itself.
            // Then it's possible that we are replying based on time or counters or other triggers.
            // TODO: Log the other reasons too.
            _logger.LogInformation("Reason (trigger) for replying: {Reason}", reason);

            // TODO: Consider doing that at the very end.
            _logger.LogDebug("Marking context as processed.");
            _botContext.Process(snapshot);

            _logger.LogTrace("Initiating Typing or Voice sending imitator.");
            await using var typing = _typingImitatorFactory.ImitateTyping(_chat.ChatId, isVoice);

            // Get context for this bot for the AI client.
            var fullContext = _chat.GetContextSnapshot();
            var context = _contextReducer.GetReducedContext(fullContext);

            _logger.LogDebug("Context for AI: {@Context}", context);

            var aiGeneratedTextResponse = _config.NotAnAssistant
                ? await _contextPreserverClient.GetTextResponseAsync(context)
                : await _aiClient.GetTextResponseAsync(context);

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

            if (_config.NotAnAssistant && _contextPreserverClient.IsBadResponse(aiGeneratedTextResponse))
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

    // TODO: Consider the situation: You write A, bot starts to type, you write B while bot is typing, bot writes C.
    // The context is as follows: You:A, You:B, Bot:C. But bot did not respond to your B message, he replied to your A message.
    // Currently he will NOT proceed to reply to your B message because the last message is HIS one.
    private List<FoulMessage>? GetSnapshotIfNeedToReply(FoulMessage triggeredByMessage)
    {
        _logger.LogTrace("Getting current context snapshot.");
        var snapshot = _botContext.GetUnprocessedSnapshot();

        if (!snapshot.Any())
        {
            _logger.LogDebug("There are no unprocessed messages. Skipping replying.");
            return null;
        }

        // Do not allow sending multiple messages. Just skip it till the next one arrives.
        if (snapshot.LastOrDefault() != null && snapshot[^1].SenderName == _config.BotName)
        {
            _logger.LogInformation("The last message in context is from the same bot. Skipping replying to it.");
            return null;
        }

        if (!_messageRespondStrategy.ShouldRespond(snapshot) && _replyEveryMessagesCounter < _randomReplyEveryMessages && triggeredByMessage.Id != "ByTime")
        {
            _logger.LogDebug("There are no messages that need processing (no keywords, no replies, no counters). Skipping replying.");
            return null;
        }

        if (snapshot[^1].IsOriginallyBotMessage && _botToBotCommunicationCounter >= _config.BotOnlyMaxMessagesBetweenDebounce)
        {
            _logger.LogInformation("Last message is from a bot. Exceeded bot-to-bot messages {Count}. Waiting for {Seconds} seconds for counter to decrease by 1. Skipping replying.", _config.BotOnlyMaxMessagesBetweenDebounce, _config.DecrementBotToBotCommunicationCounterIntervalSeconds);
            return null;
        }

        return snapshot;
    }
}
