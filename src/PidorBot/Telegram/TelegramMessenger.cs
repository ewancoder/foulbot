using System.Text;
using PidorBot.Chats;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace PidorBot.Telegram;

public sealed class TelegramMessenger : IMessenger
{
    // Telegram limit is 4096 characters.
    private const int MaxMessageLength = 4000;

    // Everything else that MarkdownV2 requires to escape. Formatting characters (*_~`) are left, so the bot can use them.
    private static readonly char[] _escapedCharacters = ['#', '[', ']', '(', ')', '>', '+', '-', '=', '|', '{', '}', '.', '!'];

    private readonly ILogger<TelegramMessenger> _logger;
    private readonly ITelegramBotClient _client;

    public TelegramMessenger(ILogger<TelegramMessenger> logger, ITelegramBotClient client)
    {
        _logger = logger;
        _client = client;
    }

    public Task SendTextAsync(ChatRef chat, string text, CancellationToken cancellationToken) => GuardAsync(chat, async () =>
    {
        foreach (var part in Split(text))
        {
            try
            {
                await _client.SendMessage(chat.Id, EscapeMarkdown(part), ParseMode.MarkdownV2, cancellationToken: cancellationToken);
            }
            catch (ApiRequestException exception) when (exception.ErrorCode == 400 && exception.Message.Contains("parse entities", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(exception, "Could not send markdown, sending plain text");
                await _client.SendMessage(chat.Id, part, cancellationToken: cancellationToken);
            }
        }
    });

    public Task SendVoiceAsync(ChatRef chat, Stream voice, CancellationToken cancellationToken)
        => GuardAsync(chat, () => _client.SendVoice(chat.Id, InputFile.FromStream(voice, "voice.ogg"), cancellationToken: cancellationToken));

    public Task SendStickerAsync(ChatRef chat, string stickerId, CancellationToken cancellationToken)
        => GuardAsync(chat, () => _client.SendSticker(chat.Id, InputFile.FromFileId(stickerId), cancellationToken: cancellationToken));

    public Task SendPhotoAsync(ChatRef chat, byte[] photo, string? caption, CancellationToken cancellationToken)
        => GuardAsync(chat, async () =>
        {
            using var stream = new MemoryStream(photo);
            await _client.SendPhoto(chat.Id, InputFile.FromStream(stream, "picture.png"), caption, cancellationToken: cancellationToken);
        });

    public Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken cancellationToken)
        => GuardAsync(chat, () => _client.SetMessageReaction(
            chat.Id, messageId, [new ReactionTypeEmoji { Emoji = emoji }], cancellationToken: cancellationToken));

    public Task NotifyTypingAsync(ChatRef chat, bool isVoice, CancellationToken cancellationToken)
        => GuardAsync(chat, () => _client.SendChatAction(
            chat.Id, isVoice ? ChatAction.RecordVoice : ChatAction.Typing, cancellationToken: cancellationToken));

    public async Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await _client.GetInfoAndDownloadFile(fileId, stream, cancellationToken);
        return stream.ToArray();
    }

    internal static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text);
        foreach (var character in _escapedCharacters)
            sb.Replace($"{character}", $@"\{character}");

        return sb.ToString();
    }

    internal static IEnumerable<string> Split(string text)
    {
        while (text.Length > MaxMessageLength)
        {
            var cut = text.LastIndexOf('\n', MaxMessageLength);
            if (cut <= 0)
                cut = text.LastIndexOf(' ', MaxMessageLength);
            if (cut <= 0)
                cut = MaxMessageLength;

            yield return text[..cut];
            text = text[cut..].TrimStart();
        }

        if (text.Length > 0)
            yield return text;
    }

    private static async Task GuardAsync(ChatRef chat, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiRequestException exception) when (exception.ErrorCode == 403
            || (exception.ErrorCode == 400 && exception.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ChatUnavailableException($"Can't write to chat {chat}.", exception);
        }
    }
}
