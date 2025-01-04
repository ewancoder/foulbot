
using FoulBot.Domain.Connections;

namespace FoulBot.Domain.Features;

public interface IStandaloneBotHandlerFactory
{
    IStandaloneBotHandler CreateStandaloneBotHandler(
        ChatScopedBotMessenger botMessenger,
        IFoulAIClient foulAIClient);
}

public interface IStandaloneBotHandler
{
    ValueTask HandleMessage(FoulMessage message);
}

/// <summary>
/// Standalone bots do not use automatic talking feature, but instead are completely controllable by user code.
/// </summary>
public sealed class StandaloneFeature : IBotFeature
{
    private readonly IStandaloneBotHandler _handler;

    public StandaloneFeature(IStandaloneBotHandler handler)
    {
        _handler = handler;
    }

    public async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        await _handler.HandleMessage(message);

        return true;
    }

    public ValueTask StopFeatureAsync() => default;
}
