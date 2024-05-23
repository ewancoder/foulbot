using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public interface IBotMessenger
{
    ValueTask<bool> CheckCanWriteAsync(FoulChatId chatId);
    ValueTask SendStickerAsync(FoulChatId chatId, string stickerId);
    ValueTask SendTextMessageAsync(FoulChatId chatId, string message);
    ValueTask SendVoiceMessageAsync(FoulChatId chatId, Stream message);
    ValueTask NotifyRecordingVoiceAsync(FoulChatId chatId);
    ValueTask NotifyTyping(FoulChatId chatId);
}

public sealed class BotMessenger : IBotMessenger
{
    private readonly ILogger<BotMessenger> _logger;
    private readonly ITelegramBotClient _client;

    public BotMessenger(
        ILogger<BotMessenger> logger,
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
