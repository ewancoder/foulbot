namespace FoulBot.Domain;

public interface IContextPreserverClient
{
    ValueTask<string> GetTextResponseAsync(List<FoulMessage> context);
    bool IsBadResponse(string message);
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

    private readonly Random _random = new Random();
    private readonly ILogger<ContextPreserverClient> _logger;
    private readonly IFoulAIClient _client;
    private readonly string _directive;

    public ContextPreserverClient(
        ILogger<ContextPreserverClient> logger,
        IFoulAIClientFactory clientFactory,
        string directive,
        string openAiModel)
    {
        _logger = logger;
        _client = clientFactory.Create(openAiModel);
        _directive = directive;
    }

    public async ValueTask<string> GetTextResponseAsync(List<FoulMessage> context)
    {
        var aiGeneratedTextResponse = await _client.GetTextResponseAsync(context);

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
            await Task.Delay(_random.Next(1100, 2300));
            context.Add(new FoulMessage(
                "Directive", FoulMessageType.System, "System", _directive, DateTime.MinValue, false));

            aiGeneratedTextResponse = await _client.GetTextResponseAsync(context);
        }

        return aiGeneratedTextResponse;
    }

    public bool IsBadResponse(string message)
    {
        return _failedContext.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            && !_failedContextCancellation.Any(keyword => message.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
    }
}
