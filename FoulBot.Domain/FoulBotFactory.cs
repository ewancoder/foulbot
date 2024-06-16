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
    private readonly IFoulAIClient _aiClient;
    private readonly IGoogleTtsService _googleTtsService;

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        ILogger<TypingImitator> typingImitatorLogger,
        IFoulAIClient aiClient,
        IGoogleTtsService googleTtsService)
    {
        _logger = logger;
        _typingImitatorLogger = typingImitatorLogger;
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

        var respondStrategy = new RespondStrategy(
            configuration, chat.IsPrivateChat);

        return new FoulBot(
            _logger,
            _aiClient,
            _googleTtsService,
            botMessenger,
            configuration,
            typingImitatorFactory,
            chat,
            respondStrategy);
    }
}
