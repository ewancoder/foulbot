namespace PidorBot.Brain;

/// <summary>
/// Catches replies where the LLM broke the character: apologized, refused, offered help.
/// </summary>
public static class ReplyFilter
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

    // Excuses about links are fine.
    private static readonly string[] _goodContext = [
        "ссылок", "ссылк", "просматривать ссыл", "просматривать содерж",
        "просматривать контент", "прости, но"
    ];

    public static bool IsGood(string reply)
        => !string.IsNullOrWhiteSpace(reply) && !IsBad(reply);

    private static bool IsBad(string reply)
        => _badContext.Any(keyword => reply.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            && !_goodContext.Any(keyword => reply.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
}
