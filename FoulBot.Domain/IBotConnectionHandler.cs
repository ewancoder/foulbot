namespace FoulBot.Domain;

/// <param name="Type">Bot type. Keyed <see cref="IBotConnectionHandler"/>
/// should be registered with this type as key.</param>
public sealed record BotConnectionConfiguration(
    string Type,
    string ConnectionString,
    FoulBotConfiguration Configuration);

public interface IBotConnectionHandler
{
    /// <summary>
    /// Starts handling bot communication. Should disconnect when cancellation is requested.
    /// Should not block.
    /// </summary>
    ValueTask<IBotMessenger> StartHandlingAsync(
        BotConnectionConfiguration configuration,
        CancellationToken cancellationToken);
}
