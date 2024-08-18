namespace FoulBot.Domain;

public interface IMessageFilter
{
    bool IsGoodMessage(string message);
}

public sealed class AssistantMessageFilter : IMessageFilter
{
    public bool IsGoodMessage(string message) => true;
}

public sealed class FoulMessageFilter : IMessageFilter
{
    private static readonly string[] _badContext = [
        "извините", "sorry", "простите", "не могу продолжать",
        "не могу участвовать", "давайте воздержимся", "прошу прощения",
        "прошу вас выражаться", "извини", "прости", "не могу помочь",
        "не могу обсуждать", "мои извинения", "нужно быть доброжелательным",
        "не думаю, что это хороший тон", "уважительным по отношению к другим",
        "поведения не терплю",
        "stop it right there", "I'm here to help", "with any questions",
        "can assist", "не будем оскорблять", "не буду оскорблять", "не могу оскорблять",
        "нецензурн", "готов помочь", "с любыми вопросами", "за грубость",
        "не буду продолжать", "призываю вас к уважению", "уважению друг к другу"
    ];

    private static readonly string[] _goodContext = [
        "ссылок", "ссылк", "просматривать ссыл", "просматривать содерж",
        "просматривать контент", "прости, но"
    ];


    public bool IsGoodMessage(string message) => !IsBadMessage(message);

    private static bool IsBadMessage(string message)
    {
        return _badContext.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            && !_goodContext.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
    }
}
