namespace FoulBot.Domain.Connections;

public interface IDuplicateMessageHandler
{
    FoulMessage Merge(IEnumerable<FoulMessage> messages);
}
