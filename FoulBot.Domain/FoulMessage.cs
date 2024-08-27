namespace FoulBot.Domain;

public sealed record Attachment(
    string? Name, Stream Data);

public enum FoulMessageType
{
    Text = 1,
    Document = 2
}

/// <summary>
/// Id should be implementation-agnostic UNIQUE value between messages.
/// </summary>
public sealed record FoulMessage(
    string Id,
    FoulMessageType Type,
    FoulMessageSenderType SenderType,
    ChatParticipant Sender,
    string Text,
    DateTime Date,
    bool IsOriginallyBotMessage,
    string? ReplyTo,
    IEnumerable<Attachment> Attachments)
{
    public string SenderName => Sender.Name;

    public FoulMessage AsUser()
    {
        return this with
        {
            SenderType = FoulMessageSenderType.User
        };
    }

    /// <summary>
    /// A special HACK flag to force always getting a valid context from BotReplyStrategy.
    /// So, any bot will ALWAYS reply to this message.
    /// </summary>
    public bool ForceReply { get; init; }

    public override string ToString()
    {
        return $"({Id}) {SenderType}.{SenderName}: {Text}";
    }

    public static FoulMessage CreateText(
        string id,
        FoulMessageSenderType senderType,
        ChatParticipant sender,
        string text,
        DateTime date,
        bool isOriginallyBotMessage,
        string? replyTo) => new(
            id, FoulMessageType.Text, senderType, sender, text, date, isOriginallyBotMessage, replyTo, Enumerable.Empty<Attachment>());

    public static FoulMessage CreateDocument(
        string id,
        FoulMessageSenderType senderType,
        ChatParticipant sender,
        DateTime date,
        bool isOriginallyBotMessage,
        string? replyTo,
        IEnumerable<Attachment> attachments) => new(
            id, FoulMessageType.Document, senderType, sender, "*sent document*", date, isOriginallyBotMessage, replyTo, attachments);
}
