namespace FoulBot.Domain;

public interface IBotReplyStrategy
{
    IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage, DateTime? notEarlierThan = null);
}

public sealed class BotReplyStrategy : IBotReplyStrategy
{
    public static readonly TimeSpan MinimumTimeBetweenMessages = TimeSpan.FromHours(1);

    private readonly ILogger<BotReplyStrategy> _logger;
    private readonly IContextReducer _contextReducer;
    private readonly TimeProvider _timeProvider;
    private readonly IFoulChat _chat;
    private readonly FoulBotConfiguration _config;
    private string? _lastProcessedMessageId;
    private DateTime _lastTriggeredAt;

    public BotReplyStrategy(
        ILogger<BotReplyStrategy> logger,
        IContextReducer contextReducer,
        TimeProvider timeProvider,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        _logger = logger;
        _contextReducer = contextReducer;
        _timeProvider = timeProvider;
        _chat = chat;
        _config = config;
    }

    public IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage, DateTime? notEarlierThan = null)
    {
        _logger.LogDebug("Getting context for replying to message");

        var context = _chat.GetContextSnapshot();

        // Forcing a reply even when all messages have already been processed
        // or the last message is a bot message etc. We ALWAYS reply.
        if (currentMessage.ForceReply)
        {
            _logger.LogDebug("Forcing a reply from the bot");
            return _contextReducer.Reduce(context);
        }

        var unprocessedMessages = context
            .SkipWhile(message => _lastProcessedMessageId != null && message.Id != _lastProcessedMessageId)
            .Skip(_lastProcessedMessageId != null ? 1 : 0)
            .ToList();

        if (unprocessedMessages.Count == 0)
        {
            _logger.LogDebug("No unprocessed messages found, skipping");
            return null;
        }

        if (currentMessage.SenderName == _config.BotName
            && currentMessage.IsOriginallyBotMessage)
        {
            _logger.LogDebug("Last message was sent by the same bot, skipping");
            return null; // Do not reply to yourself.
        }

        // Reply to every message in private chat, and to Replies.
        if (_chat.IsPrivateChat || currentMessage.ReplyTo == _config.BotId)
        {
            _logger.LogDebug("Chat is private, replying to (every) message");
            _lastProcessedMessageId = context[^1].Id;
            return _contextReducer.Reduce(context);
        }

        // HACK: Duplicated code with triggerMessage.
        // TODO: Refactor this to not iterate on context multiple times.
        // And to potentially iterate only as last resort.
        var alwaysTriggerMessage = unprocessedMessages
            .Find(_contextReducer.ShouldAlwaysTrigger);

        if (alwaysTriggerMessage != null)
        {
            _logger.LogDebug("Trigger message found: {TriggerMessage}", alwaysTriggerMessage);
            _lastProcessedMessageId = context[^1].Id;
            return _contextReducer.Reduce(context);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastTriggeredAt < MinimumTimeBetweenMessages)
        {
            _logger.LogDebug("Not enough time has passed since the last keyword was triggered, skipping ({Minutes} minutes remaining)", (MinimumTimeBetweenMessages - (now - _lastTriggeredAt)).TotalMinutes);

            // Still consider all messages processed at this point,
            // so that when _minimumTimeBetweenMessages passes we don't reply instantly to old messages.
            _lastProcessedMessageId = context[^1].Id;
            return null; // Reply to triggers only once per _minimumTimeBetweenMessages.
        }

        // TODO: Handle potential situation when there is no _lastProcessedMessageId in messages.
        var keywordMessage = context
            .SkipWhile(message => _lastProcessedMessageId != null && message.Id != _lastProcessedMessageId)
            .Skip(_lastProcessedMessageId != null ? 1 : 0)
            .FirstOrDefault(_contextReducer.ShouldTrigger);

        if (keywordMessage == null)
        {
            _logger.LogDebug("Keyword message is not foound. Skipping");
            return null;
        }

        _logger.LogDebug("Keyword message found: {KeywordMessage}", keywordMessage);

        // If something bad happens later we don't re-process old messages.
        _lastProcessedMessageId = context[^1].Id;
        _lastTriggeredAt = _timeProvider.GetUtcNow().UtcDateTime;
        return _contextReducer.Reduce(context);
    }
}
