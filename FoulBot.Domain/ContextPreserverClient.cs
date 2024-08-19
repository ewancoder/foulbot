namespace FoulBot.Domain;

public sealed class ContextPreservingFoulAIClient : IFoulAIClient
{
    private readonly IContextPreserverClient _contextPreserver;
    private readonly IFoulAIClient _foulAiClient;

    public ContextPreservingFoulAIClient(
        IContextPreserverClient contextPreserver,
        IFoulAIClient foulAiClient)
    {
        _contextPreserver = contextPreserver;
        _foulAiClient = foulAiClient;
    }

    public ValueTask<Stream> GetAudioResponseAsync(string text)
        => _foulAiClient.GetAudioResponseAsync(text);

    public ValueTask<string> GetCustomResponseAsync(string directive)
        => _foulAiClient.GetCustomResponseAsync(directive);

    public ValueTask<string> GetTextResponseAsync(IEnumerable<FoulMessage> context)
        => _contextPreserver.GetTextResponseAsync(_foulAiClient, context);
}

public interface IContextPreserverClient
{
    ValueTask<string> GetTextResponseAsync(IFoulAIClient client, IEnumerable<FoulMessage> context);
    //bool IsBadResponse(string message);
}

public sealed class ContextPreserverClient : IContextPreserverClient
{
    private static readonly string[] _failedContext = [
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
    private static readonly string[] _failedContextCancellation = [
        "ссылок", "ссылк", "просматривать ссыл", "просматривать содерж",
        "просматривать контент", "прости, но"
    ];

    private readonly ILogger<ContextPreserverClient> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly string _directive;

    public ContextPreserverClient(
        ILogger<ContextPreserverClient> logger,
        ISharedRandomGenerator random,
        string directive)
    {
        _logger = logger;
        _random = random;
        _directive = directive;
    }

    public async ValueTask<string> GetTextResponseAsync(
        IFoulAIClient client, IEnumerable<FoulMessage> context)
    {
        var aiGeneratedTextResponse = await client.GetTextResponseAsync(context);

        var i = 1;
        while (IsBadResponse(aiGeneratedTextResponse))
        {
            // When we generated 3 responses already, and they are all bad.
            if (i >= 3)
            {
                // Used in order not to add this message to the context. It will spoil the context for future requests.
                _logger.LogWarning("Generated broken context message: {Message}. NOT adding it to context, but sending it to the user cause it was the last attempt.", aiGeneratedTextResponse);
                break;
            }

            _logger.LogWarning("Generated broken context message: {Message}. Repeating main directive and trying to re-generate.", aiGeneratedTextResponse);

            i++;
            await Task.Delay(_random.Generate(1100, 2300));

            // TODO: Figure out if collection initializer copies the collection in memory or like LINQ.
            aiGeneratedTextResponse = await client.GetTextResponseAsync([
                new("Directive", FoulMessageType.System, "System", _directive, DateTime.MinValue, false, null),
                .. context
            ]);
        }

        return aiGeneratedTextResponse;
    }

    public static bool IsGoodResponse(string message) => !IsBadResponse(message);

    public static bool IsBadResponse(string message)
    {
        return _failedContext.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            && !_failedContextCancellation.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
    }
}
