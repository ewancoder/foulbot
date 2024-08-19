namespace FoulBot.Domain;

public interface IBotReplyStrategy
{
    IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage);
}

// TODO: Unit test the following:
// - Private chats
// - Replies
// - Debounce between triggered messages (and updating it)
// - LastProcessedMessageId
// - Remaking other bots messages into user's for that bot's eyes
public sealed class BotReplyStrategy : IBotReplyStrategy
{
    private static readonly TimeSpan _minimumTimeBetweenMessages = TimeSpan.FromHours(1);
    private readonly TimeProvider _timeProvider;
    private readonly IFoulChat _chat;
    private readonly FoulBotConfiguration _config;
    private string? _lastProcessedMessageId;
    private DateTime _lastTriggeredAt;

    public BotReplyStrategy(
        TimeProvider timeProvider,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        _timeProvider = timeProvider;
        _chat = chat;
        _config = config;
    }

    public IList<FoulMessage>? GetContextForReplying(FoulMessage currentMessage)
    {
        if (currentMessage.SenderName == _config.BotName)
            return null; // Do not reply to yourself.

        // Reply to every message in private chat, and to Replies.
        if (_chat.IsPrivateChat || currentMessage.ReplyTo == _config.BotId)
        {
            _lastProcessedMessageId = currentMessage.Id;
            return Reduce(_chat.GetContextSnapshot());
        }

        if (_timeProvider.GetUtcNow().UtcDateTime - _lastTriggeredAt < _minimumTimeBetweenMessages)
        {
            // Still consider all messages processed at this point,
            // so that when _minimumTimeBetweenMessages passes we don't reply instantly to old messages.
            _lastProcessedMessageId = currentMessage.Id;
            return null; // Reply to triggers only once per _minimumTimeBetweenMessages.
        }

        var context = _chat.GetContextSnapshot();

        // TODO: Handle potential situation when there is no _lastProcessedMessageId in messages.
        var triggerMessage = context
            .SkipWhile(message => _lastProcessedMessageId != null && message.Id != _lastProcessedMessageId)
            .Skip(_lastProcessedMessageId != null ? 1 : 0)
            .FirstOrDefault(ShouldTrigger);

        if (triggerMessage == null)
            return null;

        // TODO: Log trigger message.

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
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    onlyAddressedToMe.Add(message.AsUser());
                else
                    onlyAddressedToMe.Add(message);

                onlyAddressedToMeCharactersCount += message.Text.Length;
            }

            if (allMessages.Count < _config.ContextSize / 2
                && allMessagesCharactersCount < _config.MaxContextSizeInCharacters / 2
                && !ShouldTrigger(message) && !IsMyOwnMessage(message))
            {
                if (message.MessageType == FoulMessageType.Bot)
                    allMessages.Add(message.AsUser());
                else
                    allMessages.Add(message);

                allMessagesCharactersCount += message.Text.Length;
            }
        }

        return
        [
            new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false),
            .. onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_config.ContextSize)
        ];
    }

    private bool ShouldTrigger(FoulMessage message)
    {
        return _config.KeyWords.Any(keyword => message.Text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _config.BotName;
    }
}
