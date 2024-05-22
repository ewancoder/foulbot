using System;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed record FoulStatusChanged(
    string WhoName,
    string? ByName,
    ChatMemberStatus Status);

public sealed record FoulMessage(
    string Id,
    FoulMessageType MessageType,
    string SenderName,
    string Text,
    DateTime Date,
    bool IsOriginallyBotMessage)
{
    public string? ReplyTo { get; set; }

    public FoulMessage AsUser()
    {
        return this with
        {
            MessageType = FoulMessageType.User
        };
    }

    public override string ToString()
    {
        return $"{Date}.{Date.Millisecond}\t\t{SenderName} - {MessageType.ToString()} - {Text} - {ReplyTo} - {(IsOriginallyBotMessage ? "bot" : "user")}";
    }

    public static FoulMessage ByTime() => new FoulMessage("ByTime", FoulMessageType.System, "ByTime", "ByTime", DateTime.UtcNow, false);
}
