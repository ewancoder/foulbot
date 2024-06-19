using Microsoft.Extensions.Logging;

namespace FoulBot.Domain;

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
    private readonly IFoulAIClient _aiClient;
    private readonly IGoogleTtsService _googleTtsService;
    private readonly IBotDelayStrategy _delayStrategy;

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        ILogger<TypingImitator> typingImitatorLogger,
        ILogger<FoulBotContext> botContextLogger,
        ILogger<ContextPreserverClient> contextPreserverClientLogger,
        IFoulAIClient aiClient,
        IGoogleTtsService googleTtsService,
        IBotDelayStrategy delayStrategy)
    {
        _logger = logger;
        _typingImitatorLogger = typingImitatorLogger;
        _botContextLogger = botContextLogger;
        _contextPreserverClientLogger = contextPreserverClientLogger;
        _aiClient = aiClient;
        _googleTtsService = googleTtsService;
        _delayStrategy = delayStrategy;
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
            _logger,
            _aiClient,
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
                _aiClient,
                configuration.Directive));
    }
}
