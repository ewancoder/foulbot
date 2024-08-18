namespace FoulBot.Domain;

public delegate ValueTask<FoulBot?> JoinBotToChatAsync(IFoulChat chat);

public interface IFoulBotFactory
{
    ValueTask<FoulBot?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config);
}

public sealed class FoulBotFactory : IFoulBotFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClientFactory _aiClientFactory;
    private readonly ILogger<TypingImitator> _typingImitatorLogger;

    public FoulBotFactory(
        TimeProvider timeProvider,
        IBotDelayStrategy botDelayStrategy,
        ISharedRandomGenerator random,
        IFoulAIClientFactory aiClientFactory,
        ILogger<TypingImitator> typingImitatorLogger)
    {
        _timeProvider = timeProvider;
        _delayStrategy = botDelayStrategy;
        _random = random;
        _aiClientFactory = aiClientFactory;
        _typingImitatorLogger = typingImitatorLogger;
    }

    /// <summary>
    /// Returns null when it's not possible to join this bot to this chat.
    /// </summary>
    public async ValueTask<FoulBot?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var canWriteToChat = await botMessenger.CheckCanWriteAsync(chat.ChatId);
        if (!canWriteToChat)
            return null;

        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);
        var replyStrategy = new BotReplyStrategy(_timeProvider, chat, config);
        var typingImitatorFactory = new TypingImitatorFactory(
            _typingImitatorLogger, botMessenger, _timeProvider, _random);
        var aiClient = _aiClientFactory.Create(config.OpenAIModel);
        IMessageFilter messageFilter = config.IsAssistant
            ? new AssistantMessageFilter()
            : new FoulMessageFilter();

        return new FoulBot(
            messenger,
            _delayStrategy,
            replyStrategy,
            typingImitatorFactory,
            _random,
            aiClient,
            messageFilter,
            chat,
            cts,
            config);
    }
}
