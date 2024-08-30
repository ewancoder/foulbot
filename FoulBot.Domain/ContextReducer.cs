namespace FoulBot.Domain;

public interface IContextReducer
{
    IList<FoulMessage> Reduce(IList<FoulMessage> context);

    bool ShouldTrigger(FoulMessage message);
    bool ShouldAlwaysTrigger(FoulMessage message);
}

public sealed class ContextReducer : IContextReducer
{
    private readonly FoulBotConfiguration _config;

    public ContextReducer(FoulBotConfiguration config)
    {
        _config = config;
    }

    public IList<FoulMessage> Reduce(IList<FoulMessage> context)
    {
        var onlyAddressedToMe = new List<FoulMessage>();
        var onlyAddressedToMeCharactersCount = 0;
        var allMessages = new List<FoulMessage>();
        var allMessagesCharactersCount = 0;

        // TODO: Consider removing attachment messages (binary data) if not removed yet.
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

    public bool ShouldTrigger(FoulMessage message)
    {
        return _config.KeyWords.Any(keyword => message.Text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase))
            || ShouldAlwaysTrigger(message);
    }

    public bool ShouldAlwaysTrigger(FoulMessage message)
    {
        return !message.IsOriginallyBotMessage && _config.Triggers.Any(trigger =>
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
