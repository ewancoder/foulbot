using FoulBot.Domain.Storage;

namespace FoulBot.Domain.Tests;

public class FoulChatTests : Testing<FoulChat>
{
    private readonly Mock<IDuplicateMessageHandler> _duplicateMessageHandler;
    private readonly Mock<IContextStore> _contextStore;
    private readonly FoulChatId _chatId;

    public FoulChatTests()
    {
        _duplicateMessageHandler = Freeze<IDuplicateMessageHandler>();
        _contextStore = Freeze<IContextStore>();
        _chatId = Fixture.Create<FoulChatId>();
        Fixture.Register(() => _chatId);
    }

    #region AddMessage and GetContext

    [Fact]
    public void GetContextSnapshot_ShouldReturnEmptyCollection_FromStart()
    {
        var sut = CreateFoulChat();

        var snapshot = sut.GetContextSnapshot();
        Assert.Empty(snapshot);
    }

    [Theory, AutoMoqData]
    public void GetContextSnapshot_ShouldReturnContext_OrderedByDate(
        IEnumerable<FoulMessage> messages)
    {
        messages = messages.OrderByDescending(x => x.Date);

        var sut = CreateFoulChat(messages);

        var snapshot = sut.GetContextSnapshot();

        Assert.Equal(messages.OrderBy(x => x.Date), snapshot);
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task GetContextSnapshot_ShouldWorkConcurrently()
    {
        // We clean up to 200, when we reach 501.
        // So: (+200, +301) 501 -> 200, (+301) 501 -> 200, (+198) = 398 at the end

        var amountOfMessagesInSnapshot = FoulChat.CleanupContextSizeLimit;
        var processed = FoulChat.CleanupContextSizeLimit;
        while (processed < 1000)
        {
            processed++;
            amountOfMessagesInSnapshot++;
            if (amountOfMessagesInSnapshot > FoulChat.MaxContextSizeLimit)
                amountOfMessagesInSnapshot = FoulChat.CleanupContextSizeLimit;
        }
        var messages = Fixture.CreateMany<FoulMessage>(1000)
            .OrderBy(x => x.Date).ToList();

        var sut = CreateFoulChat();

        var t1 = Task.Run(() =>
        {
            Parallel.ForEach(
                messages,
                ParallelOptions,
                message => sut.AddMessage(message));
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, ParallelOptions, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);


        // Hacky way to test this, because messages are cleaning up when there are too many of them.
        Assert.Equal(amountOfMessagesInSnapshot, sut.GetContextSnapshot().Count);
        // We can't test specific messages here because random ones can be removed by cleanup process.
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task GetContextSnapshot_ShouldWorkConcurrently_UntilLimit()
    {
        // This test tests specific data without cleanup process.
        var amountOfMessagesInSnapshot = FoulChat.MaxContextSizeLimit;

        var messages = Fixture.CreateMany<FoulMessage>(amountOfMessagesInSnapshot)
            .OrderBy(x => x.Date).ToList();

        var sut = CreateFoulChat();

        var t1 = Task.Run(() =>
        {
            Parallel.ForEach(messages, ParallelOptions, message => sut.AddMessage(message));
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, ParallelOptions, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);

        Assert.Equal(messages.Count, sut.GetContextSnapshot().Count);
        Assert.Equal(messages, sut.GetContextSnapshot());
    }

    // TODO: Improve this test. In order to test concurrency, we need to AT LEAST have 1000 items.
    // However due to 2000 ms delay in FoulChat the test takes 1 minute to run with 1000 items.
    // For now using 10 items just to keep the test code here.
    [Fact]
    [Trait(Category, Concurrency)]
    public async Task HandleMessageAsync_ShouldWorkConcurrently_UntilLimit()
    {
        // This test tests specific data without cleanup process.
        var amountOfMessagesInSnapshot = FoulChat.MaxContextSizeLimit;

        var messages = Fixture.Build<FoulMessage>()
            .With(x => x.Date, () => DateTime.UtcNow + Fixture.Create<TimeSpan>())
            .CreateMany(amountOfMessagesInSnapshot)
            .OrderBy(x => x.Date)
            .ToList();

        _duplicateMessageHandler.Setup(x => x.Merge(It.IsAny<IEnumerable<FoulMessage>>()))
            .Returns<IEnumerable<FoulMessage>>(messages => messages.Single());

        var sut = CreateFoulChat();

        var t1 = Parallel.ForEachAsync(messages, async (message, _) =>
        {
            var task = sut.HandleMessageAsync(message).AsTask();

            await WaitAsync();
            TimeProvider.Advance(TimeSpan.FromSeconds(2));
            await task;
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, ParallelOptions, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);

        Assert.Equal(messages.Count, sut.GetContextSnapshot().Count);
        Assert.Equal(messages, sut.GetContextSnapshot());
    }

    [Fact]
    public void AddMessage_ShouldAddMessagesToContext()
    {
        var sut = CreateFoulChat();

        sut.AddMessage(Fixture.Create<FoulMessage>());
        Assert.Single(sut.GetContextSnapshot());

        sut.AddMessage(Fixture.Create<FoulMessage>());
        Assert.Equal(2, sut.GetContextSnapshot().Count);
    }

    [Theory, AutoMoqData]
    public void AddMessage_ShouldPersistMessage(FoulMessage message)
    {
        var sut = CreateFoulChat();

        sut.AddMessage(message);
        _contextStore.Verify(x => x.SaveMessageAsync(_chatId, message));
    }

    [Theory, AutoMoqData]
    public void AddMessage_ShouldNofityAboutMessages(
        List<FoulMessage> messages)
    {
        var notified = new List<FoulMessage>();

        var sut = CreateFoulChat();

        sut.MessageReceived += (_, message) => notified.Add(message);

        sut.AddMessage(messages[0]);
        sut.AddMessage(messages[1]);

        Assert.Contains(messages[0], notified);
        Assert.Contains(messages[1], notified);
    }

    [Fact]
    public void AddMessages_ShouldClearContext_WhenItsSizeGoesBeyondLimit()
    {
        var messages = Fixture.CreateMany<FoulMessage>(FoulChat.MaxContextSizeLimit + 1)
            .OrderBy(x => x.Date).ToList();

        var sut = CreateFoulChat(messages.Take(FoulChat.MaxContextSizeLimit - 1));

        Assert.Equal(messages.Take(FoulChat.MaxContextSizeLimit - 1), sut.GetContextSnapshot());

        sut.AddMessage(messages[^2]);
        Assert.Equal(messages.Take(FoulChat.MaxContextSizeLimit), sut.GetContextSnapshot());

        sut.AddMessage(messages[^1]);
        Assert.Equal(messages.TakeLast(FoulChat.CleanupContextSizeLimit), sut.GetContextSnapshot());
    }

    [Fact]
    public async Task AddMessages_ShouldDoNothing_WhenShutdownInitiated()
    {
        var sut = CreateFoulChat();
        await sut.GracefullyCloseAsync();

        sut.AddMessage(Fixture.Create<FoulMessage>());
        Assert.Empty(sut.GetContextSnapshot());
    }

    #endregion

    #region HandleMessage

    [Theory, AutoMoqData]
    public async Task HandleMessageAsync_ShouldNotifyAboutConsolidatedMessageById_WhenMultipleDuplicatesAreSent_AndConsolidatorSaysOnlyFirstOneShouldBeProcessed(
        FoulMessage consolidatedMessage)
    {
        var messages = Fixture.Build<FoulMessage>()
            .With(x => x.Id, Fixture.Create<string>())
            .With(x => x.Date, DateTime.MaxValue)
            .CreateMany()
            .ToList();

        _duplicateMessageHandler.Setup(x => x.Merge(messages))
            .Returns(() => consolidatedMessage);

        var sut = CreateFoulChat();

        var received = new List<FoulMessage>();
        sut.MessageReceived += (_, message) => received.Add(message);

        var tasks = new List<Task>();
        foreach (var message in messages)
        {
            tasks.Add(sut.HandleMessageAsync(message).AsTask());
        }

        await WaitAsync();
        TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await Task.WhenAll(tasks);

        // Called only once.
        _duplicateMessageHandler.Verify(x => x.Merge(It.IsAny<IEnumerable<FoulMessage>>()), Times.Once);

        Assert.Single(received);
        Assert.Equal(consolidatedMessage, received.Single());
        Assert.Equal(consolidatedMessage, sut.GetContextSnapshot().Single());
    }

    [Theory, AutoMoqData]
    public async Task HandleMessageAsync_ShouldPersistConsolidatedMessage(
        FoulMessage consolidatedMessage)
    {
        var messages = Fixture.Build<FoulMessage>()
            .With(x => x.Id, Fixture.Create<string>())
            .With(x => x.Date, DateTime.MaxValue)
            .CreateMany()
            .ToList();

        _duplicateMessageHandler.Setup(x => x.Merge(messages))
            .Returns(() => consolidatedMessage);

        var sut = CreateFoulChat();

        var received = new List<FoulMessage>();

        var tasks = new List<Task>();
        foreach (var message in messages)
        {
            tasks.Add(sut.HandleMessageAsync(message).AsTask());
        }

        await WaitAsync();
        TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await Task.WhenAll(tasks);

        // Called only once.
        _contextStore.Verify(x => x.SaveMessageAsync(_chatId, consolidatedMessage));
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldSkipReplyingToMessagesOlderThan1Minute()
    {
        var newMessage = Fixture.Build<FoulMessage>()
            .With(x => x.Date, DateTime.UtcNow - TimeSpan.FromMinutes(0.8))
            .Create();

        var oldMessage = Fixture.Build<FoulMessage>()
            .With(x => x.Date, DateTime.UtcNow - TimeSpan.FromMinutes(1))
            .Create();

        var sut = CreateFoulChat();

        await sut.HandleMessageAsync(oldMessage);
        Assert.Empty(sut.GetContextSnapshot());

        var task = sut.HandleMessageAsync(newMessage);
        await WaitAsync();
        TimeProvider.Advance(TimeSpan.FromSeconds(2));

        await task;
        Assert.NotEmpty(sut.GetContextSnapshot());
    }

    [Fact]
    public async Task HandleMessageAsync_ShouldNotDoAnything_WhenShuttingDown()
    {
        var message = Fixture.Build<FoulMessage>()
            .With(x => x.Date, DateTime.MaxValue)
            .Create();

        var sut = CreateFoulChat();
        await sut.GracefullyCloseAsync();

        await sut.HandleMessageAsync(message);
        Assert.Empty(sut.GetContextSnapshot());
    }

    #endregion

    [Fact]
    public void IsPrivateChat_ShouldReturnValueFromConstruction()
    {
        Customize("isPrivateChat", true);

        var sut = CreateFoulChat();

        Assert.True(sut.IsPrivateChat);
    }

    [Theory, AutoMoqData]
    public void ChatId_ShouldReturnValueFromConstruction(FoulChatId chatId)
    {
        Customize("chatId", chatId);

        var sut = CreateFoulChat();

        Assert.Equal(chatId, sut.ChatId);
    }

    private FoulChat CreateFoulChat(IEnumerable<FoulMessage> context)
    {
        var chat = Fixture.Create<FoulChat>();
        foreach (var message in context)
            chat.AddMessage(message);

        return chat;
    }

    private FoulChat CreateFoulChat()
    {
        return Fixture.Create<FoulChat>();
    }
}
