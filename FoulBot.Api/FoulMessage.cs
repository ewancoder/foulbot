namespace FoulBot.Api;

public sealed record FoulMessage(string Id, FoulMessageType MessageType, string SenderName, string Text, string? ReplyTo)
{
    public FoulMessage AsUser()
    {
        return this with
        {
            MessageType = FoulMessageType.User
        };
    }

    public override string ToString()
    {
        return $"{SenderName} - {MessageType.ToString()} - {Text} - {ReplyTo}";
    }
}
