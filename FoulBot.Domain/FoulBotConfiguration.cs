using System;
using System.Collections.Generic;
using System.Linq;

namespace FoulBot.Domain;

public sealed record FoulBotConfiguration
    : IContextReducerConfiguration,
    IRespondStrategyConfiguration
{
    public FoulBotConfiguration(
        string botId,
        string botName,
        string directive,
        HashSet<string> keyWords)
    {
        if (botId == null || botName == null || directive == null || keyWords == null)
            throw new ArgumentException("One of the arguments is null.");

        if (!keyWords.Any())
            throw new ArgumentException("Should have at least one keyword.");

        BotId = botId;
        BotName = botName;
        Directive = directive;
        KeyWords = keyWords;
    }

    public string BotId { get; }
    public string BotName { get; }
    public string Directive { get; }
    public IEnumerable<string> KeyWords { get; }
    public int ContextSize { get; init; } = 14;
    public int MaxContextSizeInCharacters { get; } = 3000;
    public int ReplyEveryMessages { get; init; } = 20;
    public int MessagesBetweenVoice { get; init; } = 0;
    public bool UseOnlyVoice { get; init; } = false;
    public int BotOnlyMaxMessagesBetweenDebounce { get; init; } = 3;
    public int DecrementBotToBotCommunicationCounterIntervalSeconds { get; init; } = 130;
    public bool NotAnAssistant { get; init; } = true;
    public HashSet<string> Stickers { get; } = new HashSet<string>();
    public bool OnlyReadAddressedToBotMessages { get; init; }

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

    public FoulBotConfiguration SetContextSize(int contextSize)
    {
        return this with
        {
            ContextSize = contextSize
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
