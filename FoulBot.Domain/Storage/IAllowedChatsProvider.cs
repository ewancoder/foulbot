namespace FoulBot.Domain.Storage;

public interface IAllowedChatsProvider
{
    ValueTask<IEnumerable<FoulChatId>> GetAllAllowedChatsAsync();
    ValueTask<bool> IsAllowedChatAsync(FoulChatId chatId);
    ValueTask AllowChatAsync(FoulChatId chatId);
    ValueTask DisallowChatAsync(FoulChatId chatId);
}
