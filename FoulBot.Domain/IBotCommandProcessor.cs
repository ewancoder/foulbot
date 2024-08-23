namespace FoulBot.Domain;

public interface IBotCommandProcessor : IAsyncDisposable // HACK: So that the bot can dispose of them.
{
    ValueTask<bool> ProcessCommandAsync(FoulMessage message);
}
