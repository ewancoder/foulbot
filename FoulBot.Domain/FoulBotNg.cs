namespace FoulBot.Domain;

public readonly record struct ChatParticipant(string Name);

public sealed class ChatScopedBotMessenger(
    IBotMessenger messenger,
    FoulChatId chatId,
    CancellationToken cancellationToken) // TODO: Pass CancellationToken to messenger.
{
    public ValueTask<bool> CheckCanWriteAsync()
        => messenger.CheckCanWriteAsync(chatId);

    public ValueTask SendTextMessageAsync(string message)
        => messenger.SendTextMessageAsync(chatId, message);

    public ValueTask SendStickerAsync(string stickerId)
        => messenger.SendStickerAsync(chatId, stickerId);
}

public interface IFoulBotNg
{
    event EventHandler? BotFailed;
}

/// <summary>
/// Handles logic of processing messages and deciding whether to reply.
/// </summary>
public sealed class FoulBotNg : IFoulBotNg, IAsyncDisposable
{
    private readonly ChatScopedBotMessenger _botMessenger;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly IBotReplyStrategy _replyStrategy;
    private readonly ITypingImitatorFactory _typingImitatorFactory;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClient _aiClient;
    private readonly IFoulChat _chat;
    private readonly CancellationTokenSource _cts;
    private readonly FoulBotConfiguration _config;

    private FoulBotNg(
        ChatScopedBotMessenger botMessenger,
        IBotDelayStrategy delayStrategy,
        ITypingImitatorFactory typingImitatorFactory,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        IFoulChat chat,
        IBotReplyStrategy replyStrategy,
        CancellationTokenSource cts,
        FoulBotConfiguration config)
    {
        _botMessenger = botMessenger;
        _typingImitatorFactory = typingImitatorFactory;
        _delayStrategy = delayStrategy;
        _random = random;
        _aiClient = aiClient;
        _chat = chat;
        _replyStrategy = replyStrategy;
        _cts = cts;
        _config = config;
    }

    public FoulBotId BotId => _config.FoulBotId;
    public event EventHandler? BotFailed;

    /// <summary>
    /// Returns null when it's not possible to join this bot to this chat.
    /// </summary>
    public static async ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IBotDelayStrategy delayStrategy,
        IBotReplyStrategy replyStrategy,
        ITypingImitatorFactory typingImitatorFactory,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);

        try
        {
            var canWriteToChat = await messenger.CheckCanWriteAsync();
            if (!canWriteToChat)
                throw new InvalidOperationException("Bot cannot write to chat.");

            // Do not create disposable bot instance unless it can write to chat.
            return new FoulBotNg(
                messenger,
                delayStrategy,
                typingImitatorFactory,
                random,
                aiClient,
                chat,
                replyStrategy,
                cts,
                config);
        }
        catch (InvalidOperationException)
        {
            cts.Dispose();
            return null;
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }

    public async ValueTask GreetEveryoneAsync(ChatParticipant invitedBy)
    {
        if (_config.Stickers.Count != 0)
        {
            var stickerIndex = _random.Generate(0, _config.Stickers.Count - 1);
            var stickerId = _config.Stickers[stickerIndex];

            await _botMessenger.SendStickerAsync(stickerId);
        }

        var directive = $"{_config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy.Name}, tell them hello in your manner or thank the person for adding you if you feel like it.";
        var greetingsMessage = await _aiClient.GetCustomResponseAsync(directive);
        await _botMessenger.SendTextMessageAsync(greetingsMessage);
    }

    public async Task TriggerAsync(FoulMessage message)
    {
        try
        {
            var context = _replyStrategy.GetContextForReplying(message);
            if (context == null)
                return;

            // TODO: Make sure only one TriggerAsync is executing at a time, at this point.
            // !! and do not just grab lock, CANCEL all other locks if any

            // Simulate "reading" the chat.
            await _delayStrategy.DelayAsync(_cts.Token);

            // TODO: pass isVoice.
            await using var typing = _typingImitatorFactory.ImitateTyping(_chat.ChatId, false);

            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

            await typing.FinishTypingText(aiGeneratedTextResponse);

            await _botMessenger.SendTextMessageAsync(aiGeneratedTextResponse);

            // TODO: Do not add it to chat if it's bad (context preserver).
            _chat.AddMessage(new FoulMessage(
                Guid.NewGuid().ToString(),
                FoulMessageType.Bot,
                _config.BotName,
                aiGeneratedTextResponse,
                DateTime.UtcNow, // TODO: Consider using timeprovider.
                true));
        }
        catch
        {
            // TODO: Consider returning obolean from all botMessenger operations
            // instead of relying on exceptions.
            BotFailed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}

public interface IFoulBotNgFactory
{
    ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config);
}

public sealed class FoulBotNgFactory : IFoulBotNgFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClientFactory _aiClientFactory;
    private readonly ILogger<TypingImitator> _logger;

    public FoulBotNgFactory(
        TimeProvider timeProvider,
        IBotDelayStrategy botDelayStrategy,
        ISharedRandomGenerator random,
        IFoulAIClientFactory aiClientFactory,
        ILogger<TypingImitator> logger)
    {
        _timeProvider = timeProvider;
        _delayStrategy = botDelayStrategy;
        _random = random;
        _aiClientFactory = aiClientFactory;
        _logger = logger;
    }

    public ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var replyStrategy = new BotReplyStrategy(_timeProvider, chat, config);
        var typingImitatorFactory = new TypingImitatorFactory(
            _logger, botMessenger, _timeProvider, _random);

        return FoulBotNg.JoinBotToChatAsync(
            botMessenger,
            _delayStrategy,
            replyStrategy,
            typingImitatorFactory,
            _random,
            _aiClientFactory.Create(config.OpenAIModel),
            chat,
            config);
    }
}

public interface IBotReplyStrategy
{
    IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage);
}

public sealed class BotReplyStrategy : IBotReplyStrategy
{
    private static readonly TimeSpan _minimumTimeBetweenMessages = TimeSpan.FromHours(1);
    private readonly TimeProvider _timeProvider;
    private readonly IFoulChat _chat;
    private readonly FoulBotConfiguration _config;
    private string? _lastProcessedMessageId;
    private DateTime _lastTriggeredAt;

    public BotReplyStrategy(
        TimeProvider timeProvider,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        _timeProvider = timeProvider;
        _chat = chat;
        _config = config;
    }

    public IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage)
    {
        if (currentMessage.SenderName == _config.BotName)
            return null; // Do not reply to yourself.

        // Reply to every message in private chat, and to Replies.
        if (_chat.IsPrivateChat || currentMessage.ReplyTo == _config.BotId)
        {
            return Reduce(_chat.GetContextSnapshot());
        }

        if (_timeProvider.GetUtcNow().UtcDateTime - _lastTriggeredAt < _minimumTimeBetweenMessages)
            return null; // Reply to triggers only once per _minimumTimeBetweenMessages.

        var context = _chat.GetContextSnapshot();

        // TODO: Handle potential situation when there is no _lastProcessedMessageId in messages.
        var triggerMessage = context
            .SkipWhile(message => _lastProcessedMessageId != null && message.Id != _lastProcessedMessageId)
            .Skip(_lastProcessedMessageId != null ? 1 : 0)
            .FirstOrDefault(ShouldTrigger);

        if (triggerMessage == null)
            return null;

        // TODO: Log trigger message.

        // If something bad happens later we don't re-process old messages.
        _lastProcessedMessageId = context[^1].Id;
        _lastTriggeredAt = _timeProvider.GetUtcNow().UtcDateTime;
        return Reduce(context);
    }

    private List<FoulMessage> Reduce(IList<FoulMessage> context)
    {
        var onlyAddressedToMe = new List<FoulMessage>();
        var onlyAddressedToMeCharactersCount = 0;
        var allMessages = new List<FoulMessage>();
        var allMessagesCharactersCount = 0;

        // TODO: Consider storing context in reverse order too, to avoid copying it on every message.
        foreach (var message in context.Reverse())
        {
            if (onlyAddressedToMe.Count < _config.ContextSize
                && onlyAddressedToMeCharactersCount < _config.MaxContextSizeInCharacters / 2
                && (ShouldTrigger(message) || IsMyOwnMessage(message)))
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    onlyAddressedToMe.Add(message.AsUser());
                else
                    onlyAddressedToMe.Add(message);

                onlyAddressedToMeCharactersCount += message.Text.Length;
            }

            if (allMessages.Count < _config.ContextSize / 2
                && allMessagesCharactersCount < _config.MaxContextSizeInCharacters / 2
                && !ShouldTrigger(message) && !IsMyOwnMessage(message))
            {
                if (message.MessageType == FoulMessageType.Bot)
                    allMessages.Add(message.AsUser());
                else
                    allMessages.Add(message);
            }
        }

        return
        [
            new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false),
            .. onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_config.ContextSize)
        ];
    }

    private bool ShouldTrigger(FoulMessage message)
    {
        return _config.KeyWords.Any(keyword => message.Text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _config.BotName;
    }
}
