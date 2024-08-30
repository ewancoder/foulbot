using FoulBot.Domain.Connections;
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
        _chatId = Fixture.Freeze<FoulChatId>();

        Fixture.Register(() => FoulChat.CreateFoulChatAsync(
            Fixture.Create<TimeProvider>(),
            Fixture.Create<IDuplicateMessageHandler>(),
            Fixture.Create<IContextStore>(),
            Fixture.Create<ILogger<FoulChat>>(),
            Fixture.Create<FoulChatId>()).AsTask().Result);

        // Empty context by default.
        _contextStore.Setup(x => x.GetLastAsync(_chatId, FoulChat.MinContextSize))
            .Returns(() => new([]));
    }

    private FoulChat CreateFoulChat(IEnumerable<FoulMessage> context)
    {
        _contextStore.Setup(x => x.GetLastAsync(_chatId, FoulChat.MinContextSize))
            .Returns(() => new(context));

        var chat = CreateFoulChat();

        return chat;
    }

    private FoulChat CreateFoulChat(FoulChatId chatId)
    {
        Fixture.Register(() => chatId);
        return Fixture.Create<FoulChat>();
    }

    private FoulChat CreateFoulChat()
    {
        return Fixture.Create<FoulChat>();
    }

    private FoulChatId CreatePrivateChatId()
    {
        return Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, Fixture.Create<FoulBotId>())
            .Create();
    }

    private FoulChatId CreatePublicChatId()
    {
        return Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, () => null)
            .Create();
    }

    [Fact]
    public void IsPrivateChat_ShouldReturnValueBasedOnChatId()
    {
        var chatId = CreatePrivateChatId();

        var sut = CreateFoulChat(chatId);
        Assert.True(sut.IsPrivateChat);

        chatId = CreatePublicChatId();
        sut = CreateFoulChat(chatId);
        Assert.False(sut.IsPrivateChat);
    }

    [Fact]
    public void ChatId_ShouldReturnValueFromConstruction()
    {
        var sut = CreateFoulChat();

        Assert.Equal(_chatId, sut.ChatId);
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
    public void GetContextSnapshot_ShouldReturnCurrentCollection_OrderedByDate(
        IEnumerable<FoulMessage> messages)
    {
        var sut = CreateFoulChat(messages);

        var snapshot = sut.GetContextSnapshot();

        Assert.Equal(messages.OrderBy(x => x.Date), snapshot);
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task RunManyTimesConcurrently() // To make sure we test that concurrent catch.
    {
        for (var i = 0; i < 10; i++)
            await GetContextSnapshot_ShouldWorkConcurrently();
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task GetContextSnapshot_ShouldWorkConcurrently()
    {
        // This simulates messages being added and context being cleaned up.
        // We clean up back to 200, when we reach 501 (at least with current values).
        // So: (+200, +301) 501 -> 200, (+301) 501 -> 200, (+198) = 398 at the end
        var amountOfMessagesInSnapshot = FoulChat.MinContextSize;
        var processed = FoulChat.MinContextSize;
        while (processed < 1000)
        {
            processed++;
            amountOfMessagesInSnapshot++;
            if (amountOfMessagesInSnapshot > FoulChat.MaxContextSizeLimit)
                amountOfMessagesInSnapshot = FoulChat.MinContextSize;
        }

        var messages = Fixture
            .CreateMany<FoulMessage>(1000)
            .ToList();

        // Empty FoulChat.
        var sut = CreateFoulChat();

        // Add 1000 messages to context, in parallel.
        var t1 = Task.Run(() =>
        {
            Parallel.ForEach(
                messages,
                ParallelOptions,
                message => sut.AddMessage(message));
        });

        // Read 1000 times from context at the same time, in parallel.
        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, ParallelOptions, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);

        // We can't test specific messages here because random ones can be removed by cleanup process.
        // Because we are adding them all chaotically in random order.
        Assert.Equal(
            amountOfMessagesInSnapshot,
            sut.GetContextSnapshot().Count);
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task GetContextSnapshot_ShouldWorkConcurrently_TillLimit()
    {
        // This test tests specific data without cleanup process,
        // so we can assert exact messages being inserted.
        var amountOfMessagesInSnapshot = FoulChat.MaxContextSizeLimit;

        var messages = Fixture
            .CreateMany<FoulMessage>(amountOfMessagesInSnapshot)
            .ToList();

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
        Assert.Equal(messages.OrderBy(x => x.Date), sut.GetContextSnapshot());
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task HandleMessageAsync_ShouldWorkConcurrently_TillLimit()
    {
        var amountOfMessagesInSnapshot = FoulChat.MaxContextSizeLimit;

        var messages = Fixture.Build<FoulMessage>()
            .With(x => x.Date, () => DateTime.UtcNow + Fixture.Create<TimeSpan>())
            .CreateMany(amountOfMessagesInSnapshot)
            .ToList();

        _duplicateMessageHandler.Setup(x => x.Merge(It.IsAny<IEnumerable<FoulMessage>>()))
            .Returns<IEnumerable<FoulMessage>>(messages => messages.Single());

        var sut = CreateFoulChat();

        var t1 = Parallel.ForEachAsync(messages, async (message, _) =>
        {
            var task = sut.HandleMessageAsync(message).AsTask();

            await WaitAsync();
            TimeProvider.Advance(TimeSpan.FromSeconds(2)); // Consolidation delay.
            await task;
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, ParallelOptions, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);

        Assert.Equal(messages.Count, sut.GetContextSnapshot().Count);
        Assert.Equal(messages.OrderBy(x => x.Date), sut.GetContextSnapshot());
    }

    [Theory, AutoMoqData]
    public void AddMessage_ShouldAddMessagesToContext(
        IList<FoulMessage> messages)
    {
        var sut = CreateFoulChat();

        sut.AddMessage(messages[0]);
        Assert.Single(sut.GetContextSnapshot());
        Assert.Equal(messages[0], sut.GetContextSnapshot()[0]);

        sut.AddMessage(messages[1]);
        sut.AddMessage(messages[2]);
        Assert.Equal(messages.OrderBy(x => x.Date), sut.GetContextSnapshot());
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
        IList<FoulMessage> messages)
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
        var messages = Fixture
            .CreateMany<FoulMessage>(FoulChat.MaxContextSizeLimit + 1)
            .OrderBy(x => x.Date)
            .ToList();

        var sut = CreateFoulChat(messages.Take(FoulChat.MaxContextSizeLimit - 1));

        Assert.Equal(messages.Take(FoulChat.MaxContextSizeLimit - 1), sut.GetContextSnapshot());

        sut.AddMessage(messages[^2]);
        Assert.Equal(messages.Take(FoulChat.MaxContextSizeLimit), sut.GetContextSnapshot());

        sut.AddMessage(messages[^1]);
        Assert.Equal(messages.TakeLast(FoulChat.MinContextSize), sut.GetContextSnapshot());
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
        var messages = Fixture
            .Build<FoulMessage>()
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
        var messages = Fixture
            .Build<FoulMessage>()
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
        TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await WaitAsync();
        Assert.Empty(sut.GetContextSnapshot());

        var task = sut.HandleMessageAsync(newMessage);
        TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await WaitAsync();
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
}
