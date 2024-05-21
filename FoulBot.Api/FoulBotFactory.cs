using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace FoulBot.Api;

public interface IFoulBotFactory
{
    public IFoulBot Create(
        ITelegramBotClient telegramBotClient,
        FoulBotConfiguration configuration);
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
        ITelegramBotClient telegramBotClient,
        FoulBotConfiguration configuration)
    {
        return new FoulBot(
            _logger,
            _aiClient,
            telegramBotClient,
            configuration);
    }
}
