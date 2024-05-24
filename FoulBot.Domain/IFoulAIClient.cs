using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FoulBot.Domain;

public interface IFoulAIClient
{
    ValueTask<string> GetTextResponseAsync(IEnumerable<FoulMessage> context);
    ValueTask<string> GetCustomResponseAsync(string directive);
    ValueTask<Stream> GetAudioResponseAsync(string text);
}

public interface IGoogleTtsService
{
    Task<Stream> GetAudioAsync(string text);
}

public interface IBotMessenger
{
    ValueTask<bool> CheckCanWriteAsync(FoulChatId chatId);
    ValueTask SendStickerAsync(FoulChatId chatId, string stickerId);
    ValueTask SendTextMessageAsync(FoulChatId chatId, string message);
    ValueTask SendVoiceMessageAsync(FoulChatId chatId, Stream message);
    ValueTask NotifyRecordingVoiceAsync(FoulChatId chatId);
    ValueTask NotifyTyping(FoulChatId chatId);
}


public interface IFoulChat
{
    bool IsPrivateChat { get; }
    FoulChatId ChatId { get; }
    event EventHandler<FoulMessage> MessageReceived;
    event EventHandler<FoulStatusChanged> StatusChanged;

    List<FoulMessage> GetContextSnapshot();
    void ChangeBotStatus(string whoName, string? byName, BotChatStatus status);

    // TODO: Get rid of Telegram dependency for this method. For now this is the only method left that uses it.
    void HandleMessage(FoulMessage message);

    void AddMessage(FoulMessage message);
}
