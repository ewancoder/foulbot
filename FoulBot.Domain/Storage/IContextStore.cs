namespace FoulBot.Domain.Storage;

public interface IContextStore
{
    ValueTask SaveMessageAsync(FoulChatId chatId, FoulMessage message);
    ValueTask<IEnumerable<FoulMessage>> GetLastAsync(FoulChatId chatId, int amount);
}

public sealed class NoContextStore : IContextStore
{
    public ValueTask<IEnumerable<FoulMessage>> GetLastAsync(FoulChatId chatId, int amount)
    {
        return new([]);
    }

    public ValueTask SaveMessageAsync(FoulChatId chatId, FoulMessage message)
    {
        return default;
    }
}
