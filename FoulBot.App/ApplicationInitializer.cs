using FoulBot.Domain;
using FoulBot.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;

namespace FoulBot.App;

public sealed class ApplicationInitializer
{
    private readonly IConfiguration _configuration;
    private readonly ITelegramUpdateHandlerFactory _factory;
    private readonly ChatLoader _chatLoader;
    private readonly ChatPool _chatPool;
    private readonly IEnumerable<KeyValuePair<string, FoulBotConfiguration>> _botConfigs;

    public ApplicationInitializer(
        IConfiguration configuration,
        ITelegramUpdateHandlerFactory factory,
        ChatLoader chatLoader,
        ChatPool chatPool,
        IDictionary<string, FoulBotConfiguration> botConfigs)
    {
        _configuration = configuration;
        _factory = factory;
        _chatLoader = chatLoader;
        _chatPool = chatPool;
        _botConfigs = botConfigs;
    }

    public void Initialize(CancellationToken cancellationToken)
    {
        foreach (var botConfig in _botConfigs.ToDictionary()) // Make sure there are no duplicates.
        {
            InitializeBot(botConfig.Key, botConfig.Value, cancellationToken);
        }
    }

    public ValueTask GracefullyShutdownAsync()
        => _chatPool.GracefullyStopAsync();

    private void InitializeBot(
        string apiConfigurationKeyName,
        FoulBotConfiguration config,
        CancellationToken cancellationToken)
    {
        var key = _configuration[apiConfigurationKeyName]
            ?? throw new InvalidOperationException($"Could not get API key from the configuration for bot {config.BotId}.");

        var client = new TelegramBotClient(key);
        client.StartReceiving(_factory.Create(config), cancellationToken: cancellationToken);

        // Asynchronously load bot to all the chats where it's supposed to be.
        _ = _chatLoader.LoadBotToChatAsync(client, _chatPool, config);
    }
}
