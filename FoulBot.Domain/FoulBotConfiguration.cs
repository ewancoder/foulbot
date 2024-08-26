namespace FoulBot.Domain;

public sealed record FoulBotConfiguration
{
    public FoulBotConfiguration(
        string botId,
        string botName,
        string directive,
        HashSet<string> triggers,
        HashSet<string> keyWords)
    {
        if (botId == null || botName == null || directive == null || keyWords == null)
            throw new ArgumentException("One of the arguments is null.");

        FoulBotId = new FoulBotId(botId, botName);
        Directive = directive.Replace("\r", string.Empty).Replace('\n', ' ');
        KeyWords = keyWords;
        Triggers = triggers;
    }

    public bool IsPublic { get; init; } = true;
    public FoulBotId FoulBotId { get; init; }
    public string BotId => FoulBotId.BotId;
    public string BotName => FoulBotId.BotName;

    public string OpenAIModel { get; init; } = "gpt-4o-mini";
    public string Directive { get; }
    public IEnumerable<string> KeyWords { get; init; }
    public IEnumerable<string> Triggers { get; init; }
    public int ContextSize { get; init; } = 30;
    public int MaxContextSizeInCharacters { get; init; } = 8000;
    public int ReplyEveryMessages { get; init; } = 20;
    public int MessagesBetweenVoice { get; init; }
    public bool UseOnlyVoice { get; init; }
    public int BotOnlyMaxMessagesBetweenDebounce { get; init; } = 1;
    public int DecrementBotToBotCommunicationCounterIntervalSeconds { get; init; } = 8 * 60 * 60;
    public bool NotAnAssistant { get; init; } = true;
    public IList<string> Stickers { get; init; } = [];
    public bool OnlyReadAddressedToBotMessages { get; init; }
    public bool TalkOnYourOwn { get; init; } = true;

    public bool IsAssistant => !NotAnAssistant;

    public FoulBotConfiguration Private()
    {
        return this with
        {
            IsPublic = false
        };
    }

    public FoulBotConfiguration UseGpt35()
    {
        return this with
        {
            OpenAIModel = "gpt-3.5-turbo",
            ContextSize = 15,
            MaxContextSizeInCharacters = 3000
        };
    }

    public FoulBotConfiguration DoNotWriteOnYourOwn()
    {
        return this with
        {
            TalkOnYourOwn = false
        };
    }

    public FoulBotConfiguration SetOnlyReadAddressedToBotMessages()
    {
        return this with
        {
            OnlyReadAddressedToBotMessages = true
        };
    }

    /// <summary>
    /// Do not Reply without user triggering the bot.
    /// </summary>
    public FoulBotConfiguration NeverReplyOutOfTurn()
    {
        return this with
        {
            ReplyEveryMessages = 0
        };
    }

    public FoulBotConfiguration SetContextSize(int contextSize, int maxContextSizeInCharacters = 8000)
    {
        return this with
        {
            ContextSize = contextSize,
            MaxContextSizeInCharacters = maxContextSizeInCharacters
        };
    }

    public FoulBotConfiguration WithVoiceBetween(int messages)
    {
        if (messages < 0)
            throw new InvalidOperationException("Messages count cannot be negative.");

        return this with
        {
            MessagesBetweenVoice = messages
        };
    }

    public FoulBotConfiguration WithOnlyVoice()
    {
        return this with
        {
            UseOnlyVoice = true
        };
    }

    public FoulBotConfiguration ConfigureBotToBotCommunication(
        int botOnlyMaxMessagesBetweenDebounce,
        int botOnlyDecrementIntervalSeconds)
    {
        if (botOnlyMaxMessagesBetweenDebounce < 0 || botOnlyDecrementIntervalSeconds < 0)
            throw new InvalidOperationException("Negative values are invalid.");

        return this with
        {
            BotOnlyMaxMessagesBetweenDebounce = botOnlyMaxMessagesBetweenDebounce,
            DecrementBotToBotCommunicationCounterIntervalSeconds = botOnlyDecrementIntervalSeconds
        };
    }

    public FoulBotConfiguration DoNotTalkToBots()
    {
        return this with
        {
            BotOnlyMaxMessagesBetweenDebounce = 0
        };
    }

    public FoulBotConfiguration AllowBeingAnAssistant()
    {
        return this with
        {
            NotAnAssistant = false
        };
    }

    // TODO: Consider making it immutable.
    public FoulBotConfiguration AddStickers(params string[] stickers)
    {
        foreach (var sticker in stickers)
        {
            Stickers.Add(sticker);
        }

        return this;
    }
}
