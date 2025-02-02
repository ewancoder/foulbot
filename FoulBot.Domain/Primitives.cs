
namespace FoulBot.Domain;

/// <summary>
/// UserId is technical unique user name or ID - we use it to map hardcoded user names.
/// It shouldn't exist only for system messages.
/// </summary>
public readonly record struct ChatParticipant(string Name, string? UserId)
{
    public override string ToString() => Name;

    public static ChatParticipant System = new("System", null);

    public bool IsSystem => Name == "System";
}

public readonly record struct FoulChatId(string Value)
{
    public FoulBotId? FoulBotId { get; init; }

    public bool IsPrivate => FoulBotId is not null;

    public FoulChatId MakePrivate(FoulBotId foulBotId)
    {
        return this with { FoulBotId = foulBotId };
    }

    public override string ToString() => Value.ToString();
}

public readonly record struct FoulBotId(string BotId, string BotName)
{
    public override string ToString() => $"{BotId} {BotName}";
}
