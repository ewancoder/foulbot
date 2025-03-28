namespace FoulBot.Domain.Features;

public sealed class NoClutterFeature : BotFeature
{
    private readonly IFoulChat _chat;

    public NoClutterFeature(IFoulChat chat)
    {
        _chat = chat;
    }

    public override ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        // TODO: Reuse already acquired context from upstream, do not duplicate it here.
        var snapshot = _chat.GetContextSnapshot();
        var shouldIgnore = snapshot.TakeLast(3).All(x => x.IsOriginallyBotMessage);

        if (shouldIgnore)
            return new(true);

        return new(false);
    }

    public override ValueTask StopFeatureAsync()
    {
        return default;
    }
}
