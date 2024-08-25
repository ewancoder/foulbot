namespace FoulBot.Domain;

public interface IBotFeature
{
    ValueTask<bool> ProcessMessageAsync(FoulMessage message);

    /// <summary>
    /// Should be called when bot is stopped/disposed.
    /// This should also dispose of anything disposable.
    /// </summary>
    ValueTask StopFeatureAsync();
}
