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

    public FoulBotFactory(
        ILogger<FoulBot> logger,
        IFoulAIClient aiClient)
    {
        _logger = logger;
        _aiClient = aiClient;
    }

    public IFoulBot Create(
        IBotMessenger botMessenger,
        FoulBotConfiguration configuration,
        IFoulChat chat)
    {
        return new FoulBot(
            _logger,
            _aiClient,
            botMessenger,
            configuration,
            chat);
    }
}
