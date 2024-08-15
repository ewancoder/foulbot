using FoulBot.Domain;
using Telegram.Bot.Types;

namespace FoulBot.Infrastructure;

public static class ChatIdExtensions
{
    public static FoulChatId ToFoulChatId(this ChatId chatId)
    {
        var telegramChatId = chatId.Identifier?.ToString()
            ?? throw new InvalidOperationException("Cannot get chat ID. It's null.");

        return new FoulChatId(telegramChatId);
    }

    public static ChatId ToTelegramChatId(this FoulChatId chatId)
    {
        var telegramChatId = Convert.ToInt64(chatId.Value);

        return new ChatId(telegramChatId);
    }
}
