namespace FoulBot.Domain;

public interface IFoulChatFactory
{
    IFoulChat Create(FoulChatId chatId, bool isPrivate);
}

public sealed class FoulChatFactory : IFoulChatFactory
{
    private readonly ILogger<FoulChat> _foulChatLogger;

    public FoulChatFactory(ILogger<FoulChat> foulChatLogger)
    {
        _foulChatLogger = foulChatLogger;
    }

    public IFoulChat Create(FoulChatId chatId, bool isPrivate)
    {
        return new FoulChat(_foulChatLogger, chatId, isPrivate);
    }
}
