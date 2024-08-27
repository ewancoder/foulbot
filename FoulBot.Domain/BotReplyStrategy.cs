namespace FoulBot.Domain;

public interface IBotReplyStrategy
{
    IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage);
}

public sealed class BotReplyStrategy : IBotReplyStrategy
{
    public static readonly TimeSpan MinimumTimeBetweenMessages = TimeSpan.FromHours(1);

    private readonly ILogger<BotReplyStrategy> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IFoulChat _chat;
    private readonly FoulBotConfiguration _config;
    private string? _lastProcessedMessageId;
    private DateTime _lastTriggeredAt;

    public BotReplyStrategy(
        ILogger<BotReplyStrategy> logger,
        TimeProvider timeProvider,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _chat = chat;
        _config = config;
    }

    public IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage)
    {
        _logger.LogDebug("Getting context for replying to message");

        var context = _chat.GetContextSnapshot();

        // Forcing a reply even when all messages have already been processed
        // or the last message is a bot message etc. We ALWAYS reply.
        if (currentMessage.ForceReply)
        {
            _logger.LogDebug("Forcing a reply from the bot");
            return Reduce(context);
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
            return Reduce(context);
        }

        // HACK: Duplicated code with triggerMessage.
        // TODO: Refactor this to not iterate on context multiple times.
        // And to potentially iterate only as last resort.
        var alwaysTriggerMessage = unprocessedMessages
            .FirstOrDefault(ShouldAlwaysTrigger);

        if (alwaysTriggerMessage != null)
        {
            _logger.LogDebug("Trigger message found: {TriggerMessage}", alwaysTriggerMessage);
            _lastProcessedMessageId = context[^1].Id;
            return Reduce(context);
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
            .FirstOrDefault(ShouldTrigger);

        if (keywordMessage == null)
        {
            _logger.LogDebug("Keyword message is not foound. Skipping");
            return null;
        }

        _logger.LogDebug("Keyword message found: {KeywordMessage}", keywordMessage);

        // If something bad happens later we don't re-process old messages.
        _lastProcessedMessageId = context[^1].Id;
        _lastTriggeredAt = _timeProvider.GetUtcNow().UtcDateTime;
        return Reduce(context);
    }

    private List<FoulMessage> Reduce(IList<FoulMessage> context)
    {
        var onlyAddressedToMe = new List<FoulMessage>();
        var onlyAddressedToMeCharactersCount = 0;
        var allMessages = new List<FoulMessage>();
        var allMessagesCharactersCount = 0;

        // TODO: Consider storing context in reverse order too, to avoid copying it on every message.
        foreach (var message in context.Reverse())
        {
            if (onlyAddressedToMe.Count < _config.ContextSize
                && onlyAddressedToMeCharactersCount < _config.MaxContextSizeInCharacters / 2
                && (ShouldTrigger(message) || IsMyOwnMessage(message)))
            {
                if (!IsMyOwnMessage(message) && message.SenderType == FoulMessageSenderType.Bot)
                    onlyAddressedToMe.Add(message.AsUser());
                else
                    onlyAddressedToMe.Add(message);

                onlyAddressedToMeCharactersCount += message.Text.Length;
            }

            if (allMessages.Count < _config.ContextSize / 2
                && allMessagesCharactersCount < _config.MaxContextSizeInCharacters / 2
                && !ShouldTrigger(message) && !IsMyOwnMessage(message))
            {
                if (message.SenderType == FoulMessageSenderType.Bot)
                    allMessages.Add(message.AsUser());
                else
                    allMessages.Add(message);

                allMessagesCharactersCount += message.Text.Length;
            }
        }

        return
        [
            FoulMessage.CreateText("Directive", FoulMessageSenderType.System, new("System"), _config.Directive, DateTime.MinValue, false, null),
            .. onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_config.ContextSize)
        ];
    }

    private bool ShouldTrigger(FoulMessage message)
    {
        return _config.KeyWords.Any(keyword => message.Text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            || ShouldAlwaysTrigger(message);
    }

    private bool ShouldAlwaysTrigger(FoulMessage message)
    {
        return _config.Triggers.Any(trigger =>
                message.Text.Equals(trigger, StringComparison.InvariantCultureIgnoreCase)
                || message.Text.StartsWith($"{trigger} ", StringComparison.InvariantCultureIgnoreCase)
                || message.Text.EndsWith($" {trigger}", StringComparison.InvariantCultureIgnoreCase)
                || message.Text.Contains($" {trigger} ", StringComparison.InvariantCultureIgnoreCase));
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.SenderType == FoulMessageSenderType.Bot
            && message.SenderName == _config.BotName;
    }
}
