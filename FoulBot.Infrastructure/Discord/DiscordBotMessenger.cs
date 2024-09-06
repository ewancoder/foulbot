using System.Text;
using Discord;
using FoulBot.Domain.Connections;

namespace FoulBot.Infrastructure.Discord;

public interface IDiscordBotMessengerFactory
{
    IBotMessenger Create(IDiscordClient client);
}

public sealed class DiscordBotMessengerFactory : IDiscordBotMessengerFactory
{
    private readonly ILogger<DiscordBotMessenger> _logger;

    public DiscordBotMessengerFactory(ILogger<DiscordBotMessenger> logger)
    {
        _logger = logger;
    }

    public IBotMessenger Create(IDiscordClient client)
        => new DiscordBotMessenger(_logger, client);
}

public sealed class DiscordBotMessenger : IBotMessenger
{
    private readonly ILogger<DiscordBotMessenger> _logger;
    private readonly IDiscordClient _client;

    public DiscordBotMessenger(
        ILogger<DiscordBotMessenger> logger,
        IDiscordClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async ValueTask<bool> CheckCanWriteAsync(FoulChatId chatId)
    {
        try
        {
            //await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.ChooseSticker);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Error when checking whether the bot can write to the chat.");
            return false;
        }
    }

    public async ValueTask SendStickerAsync(FoulChatId chatId, string stickerId)
    {
        //await _client.SendStickerAsync(chatId.ToTelegramChatId(), InputFile.FromFileId(stickerId));
    }

    public async ValueTask SendTextMessageAsync(FoulChatId chatId, string message)
    {
        try
        {
            var escapedMessage = EscapeMarkdown(message);
            var guild = await _client.GetGuildAsync(Convert.ToUInt64(chatId.Value.Split("__")[0]));
            var channel = await guild.GetTextChannelAsync(Convert.ToUInt64(chatId.Value.Split("__")[1]));
            await channel.SendMessageAsync(message);
            //await _client.SendTextMessageAsync(chatId.ToTelegramChatId(), escapedMessage, parseMode: ParseMode.MarkdownV2);
        }
        catch (/*ApiRequestException exception*/ Exception exception)
        {
            _ = exception;
            //_logger.LogDebug(exception, "Error when sending markdown to telegram. Sending regular text now.");
            //await _client.SendTextMessageAsync(chatId.ToTelegramChatId(), message);
        }
    }

    public async ValueTask SendVoiceMessageAsync(FoulChatId chatId, Stream message)
    {
        //await _client.SendVoiceAsync(chatId.ToTelegramChatId(), InputFile.FromStream(message));
    }

    public async ValueTask NotifyRecordingVoiceAsync(FoulChatId chatId)
    {
        //await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.RecordVoice);
    }

    public async ValueTask NotifyTyping(FoulChatId chatId)
    {
        //await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.Typing);
    }

    public async ValueTask SendImageAsync(FoulChatId chatId, Stream image)
    {
        /*var file = InputFile.FromStream(image);
        await _client.SendPhotoAsync(chatId.Value, file);*/
    }

    private static string EscapeMarkdown(string text)
    {
        char[] escapedCharacters = ['#', '[', ']', '(', ')', '>', '+', '-', '=', '|', '{', '}', '.', '!'];

        var sb = new StringBuilder(text);
        foreach (var esc in escapedCharacters)
        {
            sb = sb.Replace($"{esc}", $@"\{esc}");
        }

        return sb.ToString();
    }
}
