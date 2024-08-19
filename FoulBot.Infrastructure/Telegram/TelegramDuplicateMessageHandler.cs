namespace FoulBot.Infrastructure.Telegram;

public sealed class TelegramDuplicateMessageHandler : IDuplicateMessageHandler
{
    public FoulMessage? Merge(FoulMessage previousMessage, FoulMessage newMessage)
    {
        if (previousMessage.ReplyTo != null || newMessage.ReplyTo == null)
            return null;

        return previousMessage with
        {
            ReplyTo = previousMessage.ReplyTo ?? newMessage.ReplyTo
        };
    }
}
