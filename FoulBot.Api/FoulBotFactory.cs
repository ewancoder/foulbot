using Microsoft.Extensions.Logging;

namespace FoulBot.Api;

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
    private readonly IFoulAIClient _aiClient;
    private readonly IGoogleTtsService _googleTtsService;

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        IFoulAIClient aiClient,
        IGoogleTtsService googleTtsService)
    {
        _logger = logger;
        _aiClient = aiClient;
        _googleTtsService = googleTtsService;
    }

    public IFoulBot Create(
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        IFoulChat chat)
    {
        return new FoulBot(
            _logger,
            _aiClient,
            _googleTtsService,
            botMessenger,
            configuration,
            chat);
    }
}
