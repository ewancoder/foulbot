using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UnidecodeSharpCore;

namespace FoulBot.Infrastructure.Telegram;

public interface IFoulMessageFactory
{
    ValueTask<FoulMessage?> CreateFromAsync(Message telegramMessage, TelegramBotClient client);
}

public sealed partial class FoulMessageFactory : IFoulMessageFactory
{
    [GeneratedRegex(@"[^a-zA-Z_]", RegexOptions.Compiled, matchTimeoutMilliseconds: 50)]
    private static partial Regex NotAllowedCharacters();
    private readonly ILogger<FoulMessageFactory> _logger;
    private readonly INamesStorage _namesStorage;

    public FoulMessageFactory(
        ILogger<FoulMessageFactory> logger,
        INamesStorage namesStorage)
    {
        _logger = logger;
        _namesStorage = namesStorage;
    }

    public async ValueTask<FoulMessage?> CreateFromAsync(Message telegramMessage, TelegramBotClient client)
    {
        var senderName = await GetSenderNameAsync(telegramMessage);
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
            await client.GetInfoAndDownloadFile(fileId, stream); // TODO: Pass cancellation token.

            attachments.Add(new(document.FileName, stream));

            stream.Position = 0;

            return FoulMessage.CreateDocument(
                messageId,
                FoulMessageSenderType.User,
                new(senderName, telegramMessage.From?.Username),
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
            new(senderName, telegramMessage.From?.Username),
            telegramMessage.Text,
            telegramMessage.Date,
            false,
            telegramMessage.ReplyToMessage?.From?.Username);
    }

    private static string GetUniqueMessageId(Message message)
    {
        return $"{message.From?.Id}-{message.Date.Ticks}";
    }

    private async ValueTask<string?> GetSenderNameAsync(Message message)
    {
        if (message?.From == null)
            return null;

        if (message.From.Username is not null)
        {
            var hardcodedName = await _namesStorage.GetNameAsync(message.From.Username);
            if (hardcodedName is not null)
                return hardcodedName;
        }

        var firstName = message.From.FirstName ?? string.Empty;
        firstName = NotAllowedCharacters().Replace(firstName.Unidecode(), string.Empty);

        var lastName = message.From.LastName ?? string.Empty;
        lastName = NotAllowedCharacters().Replace(lastName.Unidecode(), string.Empty);

        var userName = message.From.Username ?? string.Empty;
        userName = NotAllowedCharacters().Replace(userName.Unidecode(), string.Empty);

        var sb = new StringBuilder();
        if (firstName is not null && firstName.Length > 0)
            sb.Append(firstName + "_");

        if (lastName is not null && lastName.Length > 0)
            sb.Append(lastName + "_");

        if (sb.Length > 0)
            sb.Remove(sb.Length - 1, 1);
        else
            sb.Append(userName);

        return sb.ToString();
    }
}
