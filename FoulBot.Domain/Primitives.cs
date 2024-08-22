
namespace FoulBot.Domain;

public readonly record struct ChatParticipant(string Name);

public readonly record struct FoulChatId(string Value)
{
    public FoulBotId? FoulBotId { get; init; }

    public bool IsPrivate => FoulBotId is not null;

    public override string ToString() => Value.ToString();

    public FoulChatId MakePrivate(FoulBotId foulBotId)
    {
        return this with { FoulBotId = foulBotId };
    }
}
