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
    private readonly IFoulAIClient _aiClient;
    private readonly IGoogleTtsService _googleTtsService;

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        ILogger<TypingImitator> typingImitatorLogger,
        ILogger<FoulBotContext> botContextLogger,
        IFoulAIClient aiClient,
        IGoogleTtsService googleTtsService)
    {
        _logger = logger;
        _typingImitatorLogger = typingImitatorLogger;
        _botContextLogger = botContextLogger;
        _aiClient = aiClient;
        _googleTtsService = googleTtsService;
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

        var contextReducer = new ContextReducer(
            respondStrategy, configuration);

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
            botContext);
    }
}
