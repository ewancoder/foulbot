namespace FoulBot.Domain;

public interface IDuplicateMessageHandler
{
    FoulMessage Merge(IEnumerable<FoulMessage> messages);
}
