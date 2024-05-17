using System;

namespace FoulBot.Api;

public sealed record FoulMessage(string Id, FoulMessageType MessageType, string SenderName, string Text, string? ReplyTo, DateTime Date)
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
        return $"{Date}.{Date.Millisecond}\t\t{SenderName} - {MessageType.ToString()} - {Text} - {ReplyTo}";
    }
}
