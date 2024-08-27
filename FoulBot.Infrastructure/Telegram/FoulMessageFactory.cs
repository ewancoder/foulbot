using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Infrastructure.Telegram;

public interface IFoulMessageFactory
{
    ValueTask<FoulMessage?> CreateFromAsync(Message telegramMessage, TelegramBotClient client);
}

public sealed class FoulMessageFactory : IFoulMessageFactory
{
    private readonly ILogger<FoulMessageFactory> _logger;

    public FoulMessageFactory(ILogger<FoulMessageFactory> logger)
    {
        _logger = logger;
    }

    public async ValueTask<FoulMessage?> CreateFromAsync(Message telegramMessage, TelegramBotClient client)
    {
        var senderName = GetSenderName(telegramMessage);
        if (senderName == null)
        {
            _logger.LogWarning("Message sender name is null, skipping the message.");
            return null;
        }

        var messageId = GetUniqueMessageId(telegramMessage);

        List<Attachment> attachments = [];
        if (telegramMessage.Type == MessageType.Document)
        {
            var document = telegramMessage.Document!;
            var fileId = document.FileId;

            var stream = new MemoryStream();
            await client.GetInfoAndDownloadFileAsync(fileId, stream); // TODO: Pass cancellation token.

            attachments.Add(new(document.FileName, stream));

            stream.Position = 0;

            return FoulMessage.CreateDocument(
                messageId,
                FoulMessageSenderType.User,
                new(senderName),
                telegramMessage.Date,
                false,
                telegramMessage.ReplyToMessage?.From?.Username,
                attachments);
        }

        if (telegramMessage.Text == null)
        {
            _logger.LogWarning("Message text is null, skipping the message.");
            return null;
        }

        return FoulMessage.CreateText(
            messageId,
            FoulMessageSenderType.User,
            new(senderName),
            telegramMessage.Text,
            telegramMessage.Date,
            false,
            telegramMessage.ReplyToMessage?.From?.Username);
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
