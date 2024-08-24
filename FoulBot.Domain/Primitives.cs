
namespace FoulBot.Domain;

public readonly record struct ChatParticipant(string Name)
{
    public override string ToString() => Name;
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
