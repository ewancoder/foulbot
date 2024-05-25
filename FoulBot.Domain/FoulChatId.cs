namespace FoulBot.Domain;

public sealed class FoulChatId
{
    public FoulChatId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value.ToString();
    }
}
