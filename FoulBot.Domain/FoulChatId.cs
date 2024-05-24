namespace FoulBot.Domain;

public sealed class FoulChatId
{
    public FoulChatId(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
