namespace FoulBot.Domain;

public interface IFoulBotContext
{
    IList<FoulMessage> GetUnprocessedSnapshot();
    void Process(IList<FoulMessage> context);
}

public sealed class FoulBotContext : IFoulBotContext
{
    private readonly ILogger<FoulBotContext> _logger;
    private readonly IFoulChat _chat;
    private string? _lastProcessedMessageId;

    public FoulBotContext(
        ILogger<FoulBotContext> logger,
        IFoulChat chat)
    {
        _logger = logger;
        _chat = chat;
    }

    public IList<FoulMessage> GetUnprocessedSnapshot()
    {
        var snapshot = _chat.GetContextSnapshot();
        if (_lastProcessedMessageId != null)
        {
            snapshot = snapshot
                .SkipWhile(message => message.Id != _lastProcessedMessageId)
                .Skip(1)
                .ToList();

            _logger.LogDebug("{LastProcessedId} is not null, checking only unprocessed messages. There are {Count} of them.", _lastProcessedMessageId, snapshot.Count);
        }

        return snapshot;
    }

    public void Process(IList<FoulMessage> context)
    {
        _logger.LogDebug("Updating last processed ID: {PreviousLastProcessed} to {NewLastProcessed}.", _lastProcessedMessageId, context[^1].Id);
        _lastProcessedMessageId = context[^1].Id;
    }
}
