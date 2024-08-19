using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.App;

public sealed class ApplicationInitializer
{
    private readonly ChatPool _chatPool;
    private readonly IEnumerable<BotConnectionConfiguration> _botConfigs;
    private readonly ChatLoader _chatLoader;
    private readonly IServiceProvider _provider;

    public ApplicationInitializer(
        [FromKeyedServices("Telegram")]ChatPool chatPool,
        IEnumerable<BotConnectionConfiguration> botConfigs,
        ChatLoader chatLoader,
        IServiceProvider provider)
    {
        _chatPool = chatPool;
        _botConfigs = botConfigs;
        _chatLoader = chatLoader;
        _provider = provider;
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.WhenAll(
        _botConfigs.Select(x => StartHandlingAsync(x, cancellationToken)));

    public ValueTask GracefullyShutdownAsync()
        => _chatPool.GracefullyStopAsync();

    private async Task StartHandlingAsync(
        BotConnectionConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var key = configuration.Type;
        var handler = _provider.GetRequiredKeyedService<IBotConnectionHandler>(key);

        var botMessenger = await handler.StartHandlingAsync(configuration, cancellationToken);

        await _chatLoader.LoadBotToAllChatsAsync(botMessenger, _chatPool, configuration.Configuration, cancellationToken);
    }
}
