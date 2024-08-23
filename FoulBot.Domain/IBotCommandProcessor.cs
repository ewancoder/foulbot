namespace FoulBot.Domain;

public interface IBotCommandProcessor
{
    ValueTask<bool> ProcessCommandAsync(FoulMessage message);

    /// <summary>
    /// Should be called when bot is stopped/disposed.
    /// This should also dispose of anything disposable.
    /// </summary>
    ValueTask StopProcessingAsync();
}
