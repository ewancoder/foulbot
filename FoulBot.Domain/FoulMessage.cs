namespace FoulBot.Domain;

/// <summary>
/// Id should be implementation-agnostic UNIQUE value between messages.
/// </summary>
public sealed record FoulMessage(
    string Id,
    FoulMessageType MessageType,
    ChatParticipant Sender,
    string Text,
    DateTime Date,
    bool IsOriginallyBotMessage,
    string? ReplyTo)
{
    public string SenderName => Sender.Name;

    public FoulMessage AsUser()
    {
        return this with
        {
            MessageType = FoulMessageType.User
        };
    }

    /// <summary>
    /// A special HACK flag to force always getting a valid context from BotReplyStrategy.
    /// So, any bot will ALWAYS reply to this message.
    /// </summary>
    public bool ForceReply { get; init; }

    public override string ToString()
    {
        return $"({Id}) {MessageType}.{SenderName}: {Text}";
    }
}
