using FoulBot.Domain.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.App;

public sealed class ApplicationInitializer
{
    private readonly ChatPool _chatPool;
    private readonly ChatPool _discordChatPool;
    private readonly IEnumerable<BotConnectionConfiguration> _botConfigs;
    private readonly ChatLoader _chatLoader;
    private readonly IServiceProvider _provider;

    public ApplicationInitializer(
        [FromKeyedServices(Constants.BotTypes.Telegram)]ChatPool chatPool,
        [FromKeyedServices(Constants.BotTypes.Discord)]ChatPool discordChatPool,
        IEnumerable<BotConnectionConfiguration> botConfigs,
        ChatLoader chatLoader,
        IServiceProvider provider)
    {
        _chatPool = chatPool;
        _discordChatPool = discordChatPool;
        _botConfigs = botConfigs;
        _chatLoader = chatLoader;
        _provider = provider;
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.WhenAll(
        _botConfigs.Select(x => StartHandlingAsync(x, cancellationToken)));

    public async Task GracefullyShutdownAsync()
    {
        // TODO: Shutdown them in parallel.
        await _chatPool.GracefullyCloseAsync();
        await _discordChatPool.GracefullyCloseAsync();
    }

    private async Task StartHandlingAsync(
        BotConnectionConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var key = configuration.Type;
        var handler = _provider.GetRequiredKeyedService<IBotConnectionHandler>(key);

        var botMessenger = await handler.StartHandlingAsync(configuration, cancellationToken);

        if (configuration.Type == Constants.BotTypes.Telegram)
            await _chatLoader.LoadBotToAllChatsAsync(botMessenger, _chatPool, configuration.Configuration, cancellationToken);
        else
            await _chatLoader.LoadBotToAllChatsAsync(botMessenger, _discordChatPool, configuration.Configuration, cancellationToken);
    }
}
