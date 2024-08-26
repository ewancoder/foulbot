namespace FoulBot.Domain.Features;

public abstract class BotFeature : IBotFeature
{
    protected static string? CutKeyword(string text, string keyword)
    {
        // TODO: Make it work with 'каждый    день' so there can be many spaces within keywords.
        if (!text.Trim().StartsWith(keyword))
            return null;

        return text.Trim()[(0 + keyword.Length)..].Trim();
    }

    public abstract ValueTask<bool> ProcessMessageAsync(FoulMessage message);
    public abstract ValueTask StopFeatureAsync();
}
