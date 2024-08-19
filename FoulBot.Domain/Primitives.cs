namespace FoulBot.Domain;

public readonly record struct ChatParticipant(string Name);

public readonly record struct FoulChatId(string Value)
{
    public override string ToString() => Value.ToString();
}
