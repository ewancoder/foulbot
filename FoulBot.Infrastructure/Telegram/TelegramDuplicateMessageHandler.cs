using FoulBot.Domain.Connections;

namespace FoulBot.Infrastructure.Telegram;

public sealed class TelegramDuplicateMessageHandler : IDuplicateMessageHandler
{
    public FoulMessage Merge(IEnumerable<FoulMessage> messages)
    {
        FoulMessage? lastMessage = null;
        foreach (var message in messages)
        {
            lastMessage = message;
            if (message.ReplyTo != null)
                return message;
        }

        if (lastMessage == null)
            throw new InvalidOperationException("Messages collection is empty.");

        return lastMessage;
    }
}
