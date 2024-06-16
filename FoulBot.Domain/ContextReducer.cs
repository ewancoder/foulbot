using System;
using System.Collections.Generic;
using System.Linq;

namespace FoulBot.Domain;

public interface IContextReducerConfiguration
{
    public string BotName { get; }
    public string Directive { get; }
    public int ContextSize { get; }
    public int MaxContextSizeInCharacters { get; }
}

public interface IContextReducer
{
    public List<FoulMessage> GetReducedContext(IEnumerable<FoulMessage> fullContext);
}

public sealed class ContextReducer : IContextReducer
{
    private readonly IMessageRespondStrategy _respondStrategy;
    private readonly IContextReducerConfiguration _config;

    public ContextReducer(
        IMessageRespondStrategy respondStrategy,
        IContextReducerConfiguration config)
    {
        _respondStrategy = respondStrategy;
        _config = config;
    }

    public List<FoulMessage> GetReducedContext(IEnumerable<FoulMessage> fullContext)
    {
        var onlyAddressedToMe = fullContext
            .Where(message => _respondStrategy.ShouldRespond(message) || IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_config.ContextSize)
            .ToList();

        while (onlyAddressedToMe.Sum(x => x.Text.Length) > _config.MaxContextSizeInCharacters && onlyAddressedToMe.Count > 2)
        {
            onlyAddressedToMe.RemoveAt(0);
        }

        var allMessages = fullContext
            .Where(message => !_respondStrategy.ShouldRespond(message) && !IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_config.ContextSize / 2) // Populate only second half with these messages.
            .ToList();

        while (allMessages.Sum(x => x.Text.Length) > _config.MaxContextSizeInCharacters / 2 && allMessages.Count > 2)
        {
            allMessages.RemoveAt(0);
        }

        var combinedContext = onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_config.ContextSize)
            .ToList();

        return new[] { new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false) }
            .Concat(combinedContext)
            .ToList();
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _config.BotName;
    }
}
