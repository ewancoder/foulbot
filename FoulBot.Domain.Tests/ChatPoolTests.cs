namespace FoulBot.Domain.Tests;

public class ChatPoolTests : Testing<ChatPool>
{
    private readonly FoulChatId _chatId;
    private readonly Mock<IBotMessenger> _botMessenger;
    private readonly Mock<IDuplicateMessageHandler> _duplicateMessageHandler;
    private readonly Mock<IFoulChatFactory> _foulChatFactory;
    private readonly Mock<IFoulChat> _chat;
    private readonly Mock<IAllowedChatsProvider> _allowedChatsProvider;

    public enum ChatMethodType
    {
        HandleMessageAsync,
        InviteBotToChatAsync,
        KickBotFromChatAsync,
        GracefullyCloseAsync,
        DisposeAsync
    }

    public enum BotMethodType
    {
        TriggerAsync,
        GreetEveryoneAsync
    }

    public ChatPoolTests()
    {
        _chatId = Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, () => null)
            .Create();

        _botMessenger = Freeze<IBotMessenger>();
        _duplicateMessageHandler = Freeze<IDuplicateMessageHandler>();
        _foulChatFactory = Freeze<IFoulChatFactory>();
        _allowedChatsProvider = Freeze<IAllowedChatsProvider>();
        _chat = new Mock<IFoulChat>(); // Do not freeze IFoulChat as we need different instances for tests.
        Fixture.Register(() => new ChatScopedBotMessenger(_botMessenger.Object, _chatId, Cts.Token));

        _allowedChatsProvider.Setup(x => x.IsAllowedChatAsync(It.IsAny<FoulChatId>()))
            .Returns(() => new(true));
    }

    private void SetupChatPool()
    {
        _foulChatFactory.Setup(x => x.Create(
            _duplicateMessageHandler.Object,
            _chatId))
            .Returns(_chat.Object);

        _chat.Setup(x => x.HandleMessageAsync(It.IsAny<FoulMessage>()))
            .Callback<FoulMessage>(message => _chat.Raise(x => x.MessageReceived += null, null, message));
    }

    private ChatPool CreateChatPool()
    {
        return Fixture.Create<ChatPool>();
    }

    /// <summary>
    /// This only tests that HandleMessageAsync call is forwarded to Chat object.
    /// </summary>
    [Theory, AutoMoqData]
    public async Task HandleMessageAsync_ShouldCreateChatAndBotAndSendMessage(
        FoulBotId botId,
        FoulMessage message,
        JoinBotToChatAsync factory)
    {
        SetupChatPool();

        await using var sut = CreateChatPool();

        await sut.HandleMessageAsync(
            _chatId, botId, message, factory, Cts.Token);

        _chat.Verify(x => x.HandleMessageAsync(message));
    }

    [Theory, AutoMoqData]
    public async Task HandleMessageAsync_ShouldNotSendMessages_ToDisallowedChats_AndShouldNotCreateChat(
        FoulBotId botId,
        FoulMessage message,
        JoinBotToChatAsync factory)
    {
        SetupChatPool();

        _allowedChatsProvider.Setup(x => x.IsAllowedChatAsync(_chatId))
            .Returns(() => new(false));

        await using var sut = CreateChatPool();

        await sut.HandleMessageAsync(
            _chatId, botId, message, factory, Cts.Token);

        _chat.Verify(x => x.HandleMessageAsync(It.IsAny<FoulMessage>()), Times.Never);
        _foulChatFactory.Verify(x => x.Create(_duplicateMessageHandler.Object, _chatId), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task KickBotAsync_ShouldNotCreateChat_WhenNotAllowed(FoulBotId botId)
    {
        SetupChatPool();

        _allowedChatsProvider.Setup(x => x.IsAllowedChatAsync(_chatId))
            .Returns(() => new(false));

        await using var sut = CreateChatPool();

        await sut.KickBotFromChatAsync(_chatId, botId, Cts.Token);

        _foulChatFactory.Verify(x => x.Create(_duplicateMessageHandler.Object, _chatId), Times.Never);
    }

    /// <summary>
    /// This test also tests that bots can do this only in allowed chats.
    /// Other tests skip this check.
    /// </summary>
    [Theory]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync, BotMethodType.TriggerAsync, true)]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync, BotMethodType.TriggerAsync, false)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync, BotMethodType.GreetEveryoneAsync, true)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync, BotMethodType.GreetEveryoneAsync, false)]
    public async Task HandleMessageAsync_AndInviteBotToChat_ShouldTriggerBot_ViaSubscription_UnlessProhibited(
        ChatMethodType chatMethodType, BotMethodType botMethodType, bool isAllowedChat,
        FoulBotId botId,
        FoulMessage message,
        IFoulBot bot,
        string invitedBy)
    {
        SetupChatPool();

        JoinBotToChatAsync factory = (chat) => new(bot);

        await using var sut = Fixture.Create<ChatPool>();

        if (!isAllowedChat)
        {
            _allowedChatsProvider.Setup(x => x.IsAllowedChatAsync(_chatId))
                .Returns(() => new(false));
        }

        if (chatMethodType == ChatMethodType.HandleMessageAsync)
            await sut.HandleMessageAsync(
                _chatId, botId, message, factory, Cts.Token);

        if (chatMethodType == ChatMethodType.InviteBotToChatAsync)
            await sut.InviteBotToChatAsync(
                _chatId, botId, invitedBy, factory, Cts.Token);

        if (botMethodType == BotMethodType.TriggerAsync)
            Mock.Get(bot).Verify(x => x.TriggerAsync(message), isAllowedChat ? Times.Once : Times.Never);
        if (botMethodType == BotMethodType.GreetEveryoneAsync)
            Mock.Get(bot).Verify(x => x.GreetEveryoneAsync(new(invitedBy)), isAllowedChat ? Times.Once : Times.Never);

        if (!isAllowedChat)
        {
            // Should not create chat when not allowed.
            _foulChatFactory.Verify(x => x.Create(_duplicateMessageHandler.Object, _chatId), Times.Never);
        }
    }

    [Theory]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync, BotMethodType.TriggerAsync)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync, BotMethodType.GreetEveryoneAsync)]
    public async Task HandleMessageAsync_ShouldSubscribeToBotShutdown_AndDisposeOfBot(
        ChatMethodType chatMethodType, BotMethodType botMethodType,
        FoulBotId botId,
        FoulMessage message,
        IFoulBot bot,
        string invitedBy)
    {
        SetupChatPool();

        JoinBotToChatAsync factory = (chat) => new(bot);

        await using var sut = Fixture.Create<ChatPool>();

        if (chatMethodType == ChatMethodType.HandleMessageAsync)
            await sut.HandleMessageAsync(
                _chatId, botId, message, factory, Cts.Token);

        if (chatMethodType == ChatMethodType.InviteBotToChatAsync)
            await sut.InviteBotToChatAsync(
                _chatId, botId, invitedBy, factory, Cts.Token);

        var botMock = Mock.Get(bot);

        if (botMethodType == BotMethodType.TriggerAsync)
            botMock.Verify(x => x.TriggerAsync(message));
        if (botMethodType == BotMethodType.GreetEveryoneAsync)
            botMock.Verify(x => x.GreetEveryoneAsync(new(invitedBy)));

        botMock.ResetCalls();


        // Additional checks.

        // We are passing broken factory a second time because otherwise it will
        // just create another bot. See the next test for details.
        JoinBotToChatAsync brokenFactory = (chat) => new((IFoulBot?)null);

        botMock.Raise(x => x.Shutdown += null, EventArgs.Empty);
        await WaitAsync();

        if (chatMethodType == ChatMethodType.HandleMessageAsync)
            await sut.HandleMessageAsync(
                _chatId, botId, message, brokenFactory, Cts.Token);

        if (chatMethodType == ChatMethodType.InviteBotToChatAsync)
            await sut.InviteBotToChatAsync(
                _chatId, botId, invitedBy, brokenFactory, Cts.Token);

        botMock.Verify(x => x.TriggerAsync(message), Times.Never);
        botMock.Verify(x => x.GreetEveryoneAsync(new(invitedBy)), Times.Never);
        botMock.Verify(x => x.DisposeAsync());
    }

    [Theory]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync, BotMethodType.TriggerAsync)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync, BotMethodType.GreetEveryoneAsync)]
    public async Task HandleMessageAsync_ShouldSubscribeToBotShutdown_ButCreateAnotherBotLater(
        ChatMethodType chatMethodType, BotMethodType botMethodType,
        FoulBotId botId,
        FoulMessage message,
        IFoulBot bot,
        string invitedBy)
    {
        SetupChatPool();

        JoinBotToChatAsync factory = (chat) => new(bot);

        await using var sut = Fixture.Create<ChatPool>();

        if (chatMethodType == ChatMethodType.HandleMessageAsync)
            await sut.HandleMessageAsync(
                _chatId, botId, message, factory, Cts.Token);

        if (chatMethodType == ChatMethodType.InviteBotToChatAsync)
            await sut.InviteBotToChatAsync(
                _chatId, botId, invitedBy, factory, Cts.Token);

        var botMock = Mock.Get(bot);

        if (botMethodType == BotMethodType.TriggerAsync)
            botMock.Verify(x => x.TriggerAsync(message));
        if (botMethodType == BotMethodType.GreetEveryoneAsync)
            botMock.Verify(x => x.GreetEveryoneAsync(new(invitedBy)));

        botMock.ResetCalls();

        // Additional checks.

        JoinBotToChatAsync brokenFactory = (chat) => new((IFoulBot?)null);

        botMock.Raise(x => x.Shutdown += null, EventArgs.Empty);
        await WaitAsync();

        await sut.HandleMessageAsync(
            _chatId, botId, message, brokenFactory, Cts.Token);

        botMock.Verify(x => x.TriggerAsync(message), Times.Never);
        botMock.Verify(x => x.GreetEveryoneAsync(new(invitedBy)), Times.Never);
        botMock.Verify(x => x.DisposeAsync());

        // More checks for creation of another bot.

        if (chatMethodType == ChatMethodType.HandleMessageAsync)
            await sut.HandleMessageAsync(
                _chatId, botId, message, factory, Cts.Token);

        if (chatMethodType == ChatMethodType.InviteBotToChatAsync)
            await sut.InviteBotToChatAsync(
                _chatId, botId, invitedBy, factory, Cts.Token);

        if (botMethodType == BotMethodType.TriggerAsync)
            botMock.Verify(x => x.TriggerAsync(message), Times.Once);
        if (botMethodType == BotMethodType.GreetEveryoneAsync)
            botMock.Verify(x => x.GreetEveryoneAsync(new(invitedBy)), Times.Once);
    }

    [Theory]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync)]
    [InlineAutoMoqData(ChatMethodType.KickBotFromChatAsync)]
    public async Task AnyMethod_ShouldCreateOnlyOneChatAndReturnItLater(
        ChatMethodType methodType,
        FoulBotId botId,
        FoulMessage message,
        JoinBotToChatAsync factory,
        string invitedBy)
    {
        SetupChatPool();

        await using var sut = CreateChatPool();

        var method = async () =>
        {
            if (methodType == ChatMethodType.HandleMessageAsync)
                await sut.HandleMessageAsync(
                    _chatId, botId, message, factory, Cts.Token);

            if (methodType == ChatMethodType.InviteBotToChatAsync)
                await sut.InviteBotToChatAsync(
                    _chatId, botId, invitedBy, factory, Cts.Token);

            if (methodType == ChatMethodType.KickBotFromChatAsync)
                await sut.KickBotFromChatAsync(
                    _chatId, botId, Cts.Token);
        };

        await method();
        await method();
        await method();

        _foulChatFactory.Verify(x => x.Create(
            _duplicateMessageHandler.Object, _chatId), Times.Once);
    }

    [Theory]
    [InlineAutoMoqData(ChatMethodType.HandleMessageAsync)]
    [InlineAutoMoqData(ChatMethodType.InviteBotToChatAsync)]
    public async Task AnyMethod_ShouldCreateOnlyOneBotAndReturnItLater(
        ChatMethodType methodType,
        FoulBotId botId,
        FoulMessage message,
        string invitedBy,
        IFoulBot bot)
    {
        SetupChatPool();

        await using var sut = CreateChatPool();

        var count = 0;
        ValueTask<IFoulBot?> Factory(IFoulChat chat)
        {
            count++;
            return new(bot);
        }

        var method = async () =>
        {
            if (methodType == ChatMethodType.HandleMessageAsync)
                await sut.HandleMessageAsync(
                    _chatId, botId, message, Factory, Cts.Token);

            if (methodType == ChatMethodType.InviteBotToChatAsync)
                await sut.InviteBotToChatAsync(
                    _chatId, botId, invitedBy, Factory, Cts.Token);

            if (methodType == ChatMethodType.KickBotFromChatAsync)
                await sut.KickBotFromChatAsync(
                    _chatId, botId, Cts.Token);
        };

        await method();
        await method();
        await method();

        Assert.Equal(1, count);
    }

    [Theory, AutoMoqData]
    public async Task ShouldHaveTheSameChatForManyBots_WhenNotPrivate(
        FoulBotId botId1,
        FoulBotId botId2,
        FoulMessage message,
        IFoulBot bot1,
        IFoulBot bot2)
    {
        SetupChatPool();

        await using var sut = CreateChatPool();

        ValueTask<IFoulBot?> Factory1(IFoulChat chat) => new(bot1);
        ValueTask<IFoulBot?> Factory2(IFoulChat chat) => new(bot2);

        await sut.HandleMessageAsync(
            _chatId, botId1, message, Factory1, Cts.Token);

        Mock.Get(bot1).Verify(x => x.TriggerAsync(message), Times.Once);
        Mock.Get(bot1).ResetCalls();

        await sut.HandleMessageAsync(
            _chatId, botId2, message, Factory2, Cts.Token);

        Mock.Get(bot2).Verify(x => x.TriggerAsync(message), Times.Once);
        Mock.Get(bot1).Verify(x => x.TriggerAsync(message), Times.Once);
    }

    [Theory, AutoMoqData]
    public async Task ShouldHaveDifferentChatsForDifferentBots_WhenPrivate(
        FoulBotId botId1,
        FoulBotId botId2,
        FoulMessage message,
        IFoulBot bot1,
        IFoulBot bot2)
    {
        var chatId1 = Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, Fixture.Create<FoulBotId>())
            .Create();
        var chatId2 = chatId1 with
        {
            FoulBotId = Fixture.Create<FoulBotId>()
        };

        var chat1 = Fixture.Create<IFoulChat>();
        var chat2 = Fixture.Create<IFoulChat>();

        _foulChatFactory.Setup(x => x.Create(
            _duplicateMessageHandler.Object,
            chatId1))
            .Returns(chat1);
        _foulChatFactory.Setup(x => x.Create(
            _duplicateMessageHandler.Object,
            chatId2))
            .Returns(chat2);

        Mock.Get(chat1).Setup(x => x.HandleMessageAsync(It.IsAny<FoulMessage>()))
            .Callback<FoulMessage>(message => Mock.Get(chat1).Raise(x => x.MessageReceived += null, null, message));
        Mock.Get(chat2).Setup(x => x.HandleMessageAsync(It.IsAny<FoulMessage>()))
            .Callback<FoulMessage>(message => Mock.Get(chat2).Raise(x => x.MessageReceived += null, null, message));

        await using var sut = CreateChatPool();

        ValueTask<IFoulBot?> Factory1(IFoulChat chat) => new(bot1);
        ValueTask<IFoulBot?> Factory2(IFoulChat chat) => new(bot2);

        await sut.HandleMessageAsync(
            chatId1, botId1, message, Factory1, Cts.Token);

        Mock.Get(bot1).Verify(x => x.TriggerAsync(message), Times.Once);
        Mock.Get(bot1).ResetCalls();

        await sut.HandleMessageAsync(
            chatId2, botId2, message, Factory2, Cts.Token);

        Mock.Get(bot2).Verify(x => x.TriggerAsync(message), Times.Once);
        Mock.Get(bot1).Verify(x => x.TriggerAsync(message), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task KickBotFromChatAsync_ShouldCallGracefulShutdown(
        FoulBotId foulBotId, FoulMessage message, IFoulBot foulBot)
    {
        SetupChatPool();

        await using var sut = CreateChatPool();

        JoinBotToChatAsync factory = (chat) => new(foulBot);

        await sut.HandleMessageAsync(
            _chatId, foulBotId, message, factory, Cts.Token);

        await sut.KickBotFromChatAsync(_chatId, foulBotId, Cts.Token);

        Mock.Get(foulBot).Verify(x => x.GracefulShutdownAsync());
    }

    [Theory]
    [InlineAutoMoqData(ChatMethodType.GracefullyCloseAsync)]
    [InlineAutoMoqData(ChatMethodType.DisposeAsync)]
    public async Task GracefullyClose_AndDispose_ShouldCallGracefullyCloseOnAllChatsAndBots(
        ChatMethodType chatMethodType,
        List<FoulChatId> chatIds,
        List<FoulBotId> botIds,
        List<IFoulChat> foulChats,
        List<IFoulBot> foulBots,
        FoulMessage message)
    {
        ValueTask<IFoulBot?> Factory(IFoulChat chat)
        {
            var index = foulChats.IndexOf(chat);
            return new(foulBots[index]);
        }

        await using var sut = CreateChatPool();

        for (var i = 0; i < 3; i++)
        {
            var chatId = chatIds[i];
            var botId = botIds[i];

            _foulChatFactory.Setup(x => x.Create(
                _duplicateMessageHandler.Object, chatId))
                .Returns(foulChats[i]);

            await sut.HandleMessageAsync(
                chatId, botId, message, Factory, Cts.Token);
        }

        if (chatMethodType == ChatMethodType.GracefullyCloseAsync)
            await sut.GracefullyCloseAsync();
        if (chatMethodType == ChatMethodType.DisposeAsync)
            await sut.GracefullyCloseAsync();

        foreach (var chat in foulChats)
            Mock.Get(chat).Verify(x => x.GracefullyCloseAsync());

        foreach (var bot in foulBots)
            Mock.Get(bot).Verify(x => x.GracefulShutdownAsync());
    }
}
