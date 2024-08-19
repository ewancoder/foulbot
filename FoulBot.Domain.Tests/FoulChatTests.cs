namespace FoulBot.Domain.Tests;

public class FoulChatTests : Testing<FoulChat>
{
    private readonly Mock<IDuplicateMessageHandler> _duplicateMessageHandler;

    public FoulChatTests()
    {
        _duplicateMessageHandler = Freeze<IDuplicateMessageHandler>();
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
    public async void GetContextSnapshot_ShouldWorkConcurrently()
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
            Parallel.ForEach(messages, message => sut.AddMessage(message));
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);


        // Hacky way to test this, because messages are cleaning up when there are too many of them.
        Assert.Equal(
            messages.TakeLast(amountOfMessagesInSnapshot),
            sut.GetContextSnapshot().TakeLast(amountOfMessagesInSnapshot));
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

        await Task.WhenAll(tasks);

        // Called only once.
        _duplicateMessageHandler.Verify(x => x.Merge(It.IsAny<IEnumerable<FoulMessage>>()), Times.Once);

        Assert.Single(received);
        Assert.Equal(consolidatedMessage, received.Single());
        Assert.Equal(consolidatedMessage, sut.GetContextSnapshot().Single());
    }

    [Theory, AutoMoqData]
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

        await sut.HandleMessageAsync(newMessage);
        Assert.NotEmpty(sut.GetContextSnapshot());
    }

    [Theory, AutoMoqData]
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
