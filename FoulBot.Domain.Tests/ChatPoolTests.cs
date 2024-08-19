namespace FoulBot.Domain.Tests;

public class ChatPoolTests : Testing<ChatPool>
{
    private readonly FoulChatId _chatId;
    private readonly Mock<IBotMessenger> _botMessenger;
    private readonly Mock<IDuplicateMessageHandler> _duplicateMessageHandler;
    private readonly Mock<IFoulChatFactory> _foulChatFactory;

    public ChatPoolTests()
    {
        _botMessenger = Freeze<IBotMessenger>();
        _duplicateMessageHandler = Freeze<IDuplicateMessageHandler>();
        _foulChatFactory = Freeze<IFoulChatFactory>();
        Fixture.Register(() => new ChatScopedBotMessenger(_botMessenger.Object, _chatId, Cts.Token));
    }

    /// <summary>
    /// This only tests that HandleMessageAsync call is forwarded to Chat object.
    /// </summary>
    [Theory, AutoMoqData]
    public async Task HandleMessageAsync_ShouldCreateChatAndBotAndSendMessage(
        FoulChatId chatId, FoulBotId botId,
        FoulMessage message,
        IFoulChat chat,
        JoinBotToChatAsync factory)
    {
        _foulChatFactory.Setup(x => x.Create(
            _duplicateMessageHandler.Object,
            chatId))
            .Returns(chat);

        await using var sut = Fixture.Create<ChatPool>();

        await sut.HandleMessageAsync(
            chatId, botId, message, factory, Cts.Token);

        Mock.Get(chat).Verify(x => x.HandleMessageAsync(message));
    }
}
