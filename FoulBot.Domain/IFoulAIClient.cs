namespace FoulBot.Domain;

public interface IFoulAIClientFactory
{
    IFoulAIClient Create(string openAiModel);
}

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

    IList<FoulMessage> GetContextSnapshot();

    // TODO: Get rid of Telegram dependency for this method. For now this is the only method left that uses it.
    void HandleMessage(FoulMessage message);

    void AddMessage(FoulMessage message);

    Task GracefullyStopAsync();
}
