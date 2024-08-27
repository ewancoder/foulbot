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

    public FoulChatFactory(
        ILogger<FoulChat> foulChatLogger,
        TimeProvider timeProvider)
    {
        _foulChatLogger = foulChatLogger;
        _timeProvider = timeProvider;
    }

    public IFoulChat Create(
        IDuplicateMessageHandler duplicateMessageHandler,
        FoulChatId chatId)
    {
        return new FoulChat(_timeProvider, duplicateMessageHandler, _foulChatLogger, chatId);
    }
}
