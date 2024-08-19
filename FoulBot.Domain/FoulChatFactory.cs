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

    public FoulChatFactory(ILogger<FoulChat> foulChatLogger)
    {
        _foulChatLogger = foulChatLogger;
    }

    public IFoulChat Create(
        IDuplicateMessageHandler duplicateMessageHandler,
        FoulChatId chatId)
    {
        return new FoulChat(duplicateMessageHandler, _foulChatLogger, chatId);
    }
}
