namespace FoulBot.Domain;

public delegate IFoulBot FoulChatToBotFactory(IFoulChat chat);

public static class FoulBotFactoryExtensions
{
    public static FoulChatToBotFactory CreateBotFactoryFromChat(
        this IFoulBotFactory botFactory,
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration)
    {
        return chat => botFactory.Create(botMessenger, configuration, chat);
    }
}

public interface IFoulBotFactory
{
    public IFoulBot Create(
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        IFoulChat chat);
}

public sealed class FoulBotFactory : IFoulBotFactory
{
    private readonly ILogger<FoulBot> _logger;
    private readonly ILogger<TypingImitator> _typingImitatorLogger;
    private readonly ILogger<FoulBotContext> _botContextLogger;
    private readonly ILogger<ContextPreserverClient> _contextPreserverClientLogger;
    private readonly ILogger<ReminderCreator> _reminderLogger;
    private readonly IFoulAIClientFactory _aiClientFactory;
    private readonly IGoogleTtsService _googleTtsService;
    private readonly IBotDelayStrategy _delayStrategy;

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        ILogger<TypingImitator> typingImitatorLogger,
        ILogger<FoulBotContext> botContextLogger,
        ILogger<ContextPreserverClient> contextPreserverClientLogger,
        ILogger<ReminderCreator> reminderLogger,
        IFoulAIClientFactory aiClientFactory,
        IGoogleTtsService googleTtsService,
        IBotDelayStrategy delayStrategy)
    {
        _logger = logger;
        _typingImitatorLogger = typingImitatorLogger;
        _botContextLogger = botContextLogger;
        _contextPreserverClientLogger = contextPreserverClientLogger;
        _reminderLogger = reminderLogger;
        _aiClientFactory = aiClientFactory;
        _googleTtsService = googleTtsService;
        _delayStrategy = delayStrategy;
        // TODO: Use cancellation token.
    }

    public IFoulBot Create(
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        IFoulChat chat)
    {
        var typingImitatorFactory = new TypingImitatorFactory(
            _typingImitatorLogger, botMessenger);

        var respondStrategy = new MessageRespondStrategy(
            configuration, chat.IsPrivateChat);

        IContextReducer contextReducer = configuration.OnlyReadAddressedToBotMessages
            ? new AddressedToMeContextReducer(respondStrategy, configuration)
            : new ContextReducer(respondStrategy, configuration);

        var botContext = new FoulBotContext(
            _botContextLogger, chat);

        return new FoulBot(
            _reminderLogger,
            _logger,
            _aiClientFactory,
            _googleTtsService,
            botMessenger,
            configuration,
            typingImitatorFactory,
            chat,
            respondStrategy,
            contextReducer,
            botContext,
            _delayStrategy,
            new ContextPreserverClient(
                _contextPreserverClientLogger,
                _aiClientFactory,
                configuration.Directive,
                configuration.OpenAIModel));
    }
}
