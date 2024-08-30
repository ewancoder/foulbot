using FoulBot.Domain.Connections;
using FoulBot.Domain.Storage;

namespace FoulBot.Domain;

public interface IFoulChatFactory
{
    ValueTask<IFoulChat> CreateAsync(
        IDuplicateMessageHandler duplicateMessageHandler,
        FoulChatId chatId);
}

public sealed class FoulChatFactory : IFoulChatFactory
{
    private readonly ILogger<FoulChat> _foulChatLogger;
    private readonly TimeProvider _timeProvider;
    private readonly IContextStore _contextStore;

    public FoulChatFactory(
        ILogger<FoulChat> foulChatLogger,
        TimeProvider timeProvider,
        IContextStore contextStore)
    {
        _foulChatLogger = foulChatLogger;
        _timeProvider = timeProvider;
        _contextStore = contextStore;
    }

    public async ValueTask<IFoulChat> CreateAsync(
        IDuplicateMessageHandler duplicateMessageHandler,
        FoulChatId chatId)
    {
        return await FoulChat.CreateFoulChatAsync(
            _timeProvider,
            duplicateMessageHandler,
            _contextStore,
            _foulChatLogger,
            chatId);
    }
}
