namespace FoulBot.Domain;

public interface IBotMessenger
{
    ValueTask<bool> CheckCanWriteAsync(FoulChatId chatId);
    ValueTask SendStickerAsync(FoulChatId chatId, string stickerId);
    ValueTask SendTextMessageAsync(FoulChatId chatId, string message);
    ValueTask SendVoiceMessageAsync(FoulChatId chatId, Stream message);
    ValueTask NotifyRecordingVoiceAsync(FoulChatId chatId);
    ValueTask NotifyTyping(FoulChatId chatId);
    ValueTask SendImageAsync(FoulChatId chatId, Stream image);
}
