namespace FoulBot.Domain;

public interface IDuplicateMessageHandler
{
    /// <summary>
    /// Should return null if merging is not needed.
    /// </summary>
    FoulMessage? Merge(FoulMessage previousMessage, FoulMessage newMessage);
}
