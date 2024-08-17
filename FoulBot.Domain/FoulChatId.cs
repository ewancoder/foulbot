namespace FoulBot.Domain;

public readonly record struct FoulChatId
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
