using FoulBot.Domain.Connections;

namespace FoulBot.Infrastructure.Discord;

// TODO: Consider unifying this with Telegram and moving this to Domain.
public sealed class DiscordDuplicateMessageHandler : IDuplicateMessageHandler
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
