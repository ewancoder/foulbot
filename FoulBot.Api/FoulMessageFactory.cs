using FoulBot.Domain;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public interface IFoulMessageFactory
{
    FoulMessage? CreateFrom(Message telegramMessage);
}

public sealed class FoulMessageFactory : IFoulMessageFactory
{
    private readonly ILogger<FoulMessageFactory> _logger;

    public FoulMessageFactory(ILogger<FoulMessageFactory> logger)
    {
        _logger = logger;
    }

    public FoulMessage? CreateFrom(Message telegramMessage)
    {
        var senderName = GetSenderName(telegramMessage);
        if (telegramMessage.Text == null || senderName == null)
        {
            _logger.LogWarning("Message text or sender name are null, skipping the message.");
            return null;
        }

        var messageId = GetUniqueMessageId(telegramMessage);

        return new FoulMessage(
            messageId,
            FoulMessageType.User,
            senderName,
            telegramMessage.Text,
            telegramMessage.Date,
            false)
        {
            ReplyTo = telegramMessage.ReplyToMessage?.From?.Username
        };
    }

    private string GetUniqueMessageId(Message message)
    {
        return $"{message.From?.Id}-{message.Date.Ticks}";
    }

    private string? GetSenderName(Message message)
    {
        // TODO: Remove all unsupported characters (normalize name).
        // Maybe do this on OpenAI side.
        if (message?.From == null)
            return null;

        if (message.From.FirstName == null && message.From.LastName == null)
            return null;

        if (message.From.FirstName == null)
            return message.From.LastName;

        if (message.From.LastName == null)
            return message.From.FirstName;

        return $"{message.From.FirstName}_{message.From.LastName}";
    }
}
