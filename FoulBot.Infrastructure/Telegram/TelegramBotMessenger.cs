using FoulBot.Domain;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Infrastructure.Telegram;

public interface ITelegramBotMessengerFactory
{
    IBotMessenger Create(ITelegramBotClient client);
}

public sealed class TelegramBotMessengerFactory : ITelegramBotMessengerFactory
{
    private readonly ILogger<TelegramBotMessenger> _logger;

    public TelegramBotMessengerFactory(ILogger<TelegramBotMessenger> logger)
    {
        _logger = logger;
    }

    public IBotMessenger Create(ITelegramBotClient client)
        => new TelegramBotMessenger(_logger, client);
}

public sealed class TelegramBotMessenger : IBotMessenger
{
    private readonly ILogger<TelegramBotMessenger> _logger;
    private readonly ITelegramBotClient _client;

    public TelegramBotMessenger(
        ILogger<TelegramBotMessenger> logger,
        ITelegramBotClient client)
    {
        _logger = logger;
        _client = client;
    }

    public async ValueTask<bool> CheckCanWriteAsync(FoulChatId chatId)
    {
        try
        {
            await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.ChooseSticker);
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
        await _client.SendStickerAsync(chatId.ToTelegramChatId(), InputFile.FromFileId(stickerId));
    }

    public async ValueTask SendTextMessageAsync(FoulChatId chatId, string message)
    {
        await _client.SendTextMessageAsync(chatId.ToTelegramChatId(), message);
    }

    public async ValueTask SendVoiceMessageAsync(FoulChatId chatId, Stream message)
    {
        await _client.SendVoiceAsync(chatId.ToTelegramChatId(), InputFile.FromStream(message));
    }

    public async ValueTask NotifyRecordingVoiceAsync(FoulChatId chatId)
    {
        await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.RecordVoice);
    }

    public async ValueTask NotifyTyping(FoulChatId chatId)
    {
        await _client.SendChatActionAsync(chatId.ToTelegramChatId(), ChatAction.Typing);
    }
}
