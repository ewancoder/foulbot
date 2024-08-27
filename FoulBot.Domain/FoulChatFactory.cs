using FoulBot.Domain.Storage;

namespace FoulBot.Domain;

public interface IFoulChatFactory
{
    IFoulChat Create(
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

    public IFoulChat Create(
        IDuplicateMessageHandler duplicateMessageHandler,
        FoulChatId chatId)
    {
        return new FoulChat(
            _timeProvider,
            duplicateMessageHandler,
            _contextStore,
            _foulChatLogger,
            chatId);
    }
}
