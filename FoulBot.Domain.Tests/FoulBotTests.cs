namespace FoulBot.Domain.Tests;

public class FoulBotTests : Testing<FoulBot>
{
    private readonly Mock<IFoulChat> _chat;
    private readonly Mock<ISharedRandomGenerator> _random;
    private readonly Mock<IBotMessenger> _botMessenger;
    private readonly Mock<IFoulAIClient> _aiClient;
    private readonly Mock<IBotReplyStrategy> _replyStrategy;
    private readonly Mock<IMessageFilter> _messageFilter;
    private readonly Mock<IBotDelayStrategy> _delayStrategy;
    private readonly Mock<IReplyImitatorFactory> _replyImitatorFactory;
    private readonly Mock<IBotReplyModePicker> _replyModePicker;

    public FoulBotTests()
    {
        _chat = Freeze<IFoulChat>();
        _random = Freeze<ISharedRandomGenerator>();
        _botMessenger = Freeze<IBotMessenger>();
        _aiClient = Freeze<IFoulAIClient>();
        _replyStrategy = Freeze<IBotReplyStrategy>();
        _messageFilter = Freeze<IMessageFilter>();
        _delayStrategy = Freeze<IBotDelayStrategy>();
        _replyImitatorFactory = Freeze<IReplyImitatorFactory>();
        _replyModePicker = Freeze<IBotReplyModePicker>();

        _replyModePicker.Setup(x => x.GetBotReplyMode(It.IsAny<IList<FoulMessage>>()))
            .Returns(() => new(ReplyType.Text));

        _messageFilter.Setup(x => x.IsGoodMessage(It.IsAny<string>()))
            .Returns(true);

        Fixture.Register(() => new ChatScopedBotMessenger(
            _botMessenger.Object, _chat.Object.ChatId));
    }

    public FoulChatId ChatId => _chat.Object.ChatId;

    [Theory, AutoMoqData]
    public async Task SendRawAsync_ShouldJustSendToChat(string message)
    {
        var sut = CreateFoulBot();

        await sut.SendRawAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, message));
    }

    [Theory, AutoMoqData]
    public async Task SendRawAsync_ShouldNotAddMessageToContext(string message)
    {
        var sut = CreateFoulBot();

        await sut.SendRawAsync(message);

        _chat.Verify(x => x.AddMessage(It.IsAny<FoulMessage>()), Times.Never);
    }

    #region GreetEveryoneAsync

    [Theory, AutoMoqData]
    public async Task GreetEveryoneAsync_ShouldSendASticker_WhenPresent(
        ChatParticipant invitedBy,
        string[] stickerIds)
    {
        var config = Fixture.Build<FoulBotConfiguration>()
            .With(x => x.Stickers, stickerIds)
            .Create();

        _random.Setup(x => x.Generate(0, stickerIds.Length - 1))
            .Returns(0);

        var sut = CreateFoulBot(config);
;
        await sut.GreetEveryoneAsync(invitedBy);

        _botMessenger.Verify(x => x.SendStickerAsync(ChatId, stickerIds[0]));
    }

    [Theory, AutoMoqData]
    public async Task GreetEveryoneAsync_ShouldNotSendASticker_WhenNotPresent(
        ChatParticipant invitedBy)
    {
        var config = Fixture.Build<FoulBotConfiguration>()
            .With(x => x.Stickers, [])
            .Create();

        var sut = CreateFoulBot(config);

        await sut.GreetEveryoneAsync(invitedBy);

        _botMessenger.Verify(x => x.SendStickerAsync(It.IsAny<FoulChatId>(), It.IsAny<string>()), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task GreetEveryoneAsync_ShouldSendAGreetingsMessage(
        ChatParticipant invitedBy,
        string generatedMessage)
    {
        var config = CreateDefaultConfig();

        _aiClient.Setup(x => x.GetCustomResponseAsync(It.Is<string>(
            directive => directive.Contains(config.Directive)
                && directive.Contains(invitedBy.Name)
                && directive.Contains("You have just been added to a chat group"))))
            .Returns(() => new(generatedMessage));

        var sut = CreateFoulBot(config);

        await sut.GreetEveryoneAsync(invitedBy);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, generatedMessage));
    }

    [Theory, AutoMoqData]
    public async Task GreetEveryoneAsync_ShouldAddTheGreetingsMessageToContext(
        ChatParticipant invitedBy,
        string generatedMessage)
    {
        var config = CreateDefaultConfig();

        _aiClient.Setup(x => x.GetCustomResponseAsync(It.Is<string>(
            directive => directive.Contains(config.Directive)
                && directive.Contains(invitedBy.Name)
                && directive.Contains("You have just been added to a chat group"))))
            .Returns(() => new(generatedMessage));

        var sut = CreateFoulBot(config);

        await sut.GreetEveryoneAsync(invitedBy);

        AssertContextNotified(generatedMessage, config.BotName);
    }

    #endregion

    #region DisposeAsync

    [Theory, AutoMoqData]
    public async Task DisposeAsync_ShouldDisposeOfCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        Customize("cts", cts);

        {
            await using var sut = CreateFoulBot();
        }

        Assert.Throws<ObjectDisposedException>(
            () => cts.Token.ThrowIfCancellationRequested());
    }

    [Theory, AutoMoqData]
    public async Task DisposeAsync_ShouldCancelTheToken()
    {
        using var cts = new CancellationTokenSource();
        Customize("cts", cts);

        var isCanceled = false;
        cts.Token.Register(() => isCanceled = true);

        {
            await using var sut = CreateFoulBot();
        }

        Assert.True(isCanceled);
    }

    #endregion

    #region TriggerAsync

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldSendGeneratedReply_OnThePositiveFlow_BySendingAndImitatingText(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var sut = CreateFoulBot();

        await sut.TriggerAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, responseMessage));
    }

    #region Command processors

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldNotSendAnything_WhenAnyOfCommandProcessorsReturnTrue(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor1 = Fixture.Create<Mock<IBotFeature>>();
        var processor2 = Fixture.Create<Mock<IBotFeature>>();

        processor1.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(false));
        processor2.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));

        var sut = CreateFoulBot();
        sut.AddFeature(processor1.Object);
        sut.AddFeature(processor2.Object);

        await sut.TriggerAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, responseMessage), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldSendMessage_WhenAllCommandProcessorsReturnFalse(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor1 = Fixture.Create<Mock<IBotFeature>>();
        var processor2 = Fixture.Create<Mock<IBotFeature>>();

        processor1.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(false));
        processor2.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(false));

        var sut = CreateFoulBot();
        sut.AddFeature(processor1.Object);
        sut.AddFeature(processor2.Object);

        await sut.TriggerAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, responseMessage));
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldProcessOnlyOnce_WithCommandProcessor(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor1 = Fixture.Create<Mock<IBotFeature>>();
        var processor2 = Fixture.Create<Mock<IBotFeature>>();

        processor1.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));
        processor2.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));

        var sut = CreateFoulBot();
        sut.AddFeature(processor1.Object);
        sut.AddFeature(processor2.Object);

        await sut.TriggerAsync(message);

        processor1.Verify(x => x.ProcessMessageAsync(message));
        processor2.Verify(x => x.ProcessMessageAsync(message), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldNotifyUsers_WhenTheirPromptHasBeenHandledByAFeature(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor = Fixture.Create<Mock<IBotFeature>>();

        processor.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));

        var config = CreateDefaultConfig();
        var sut = CreateFoulBot(config);
        sut.AddFeature(processor.Object);

        await sut.TriggerAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, $"Command processed by @{config.BotId} {processor.Object.GetType().Name}"));
    }

    [Theory, AutoMoqData]
    public async Task DisposeAsync_ShouldStopAllCommandProcessors(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor1 = Fixture.Create<Mock<IBotFeature>>();
        var processor2 = Fixture.Create<Mock<IBotFeature>>();

        processor1.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));
        processor2.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));

        var sut = CreateFoulBot();
        sut.AddFeature(processor1.Object);
        sut.AddFeature(processor2.Object);

        await sut.DisposeAsync();

        processor1.Verify(x => x.StopFeatureAsync());
        processor2.Verify(x => x.StopFeatureAsync());
    }

    [Theory, AutoMoqData]
    public async Task GracefulShutdown_ShouldStopAllCommandProcessors(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var processor1 = Fixture.Create<Mock<IBotFeature>>();
        var processor2 = Fixture.Create<Mock<IBotFeature>>();

        processor1.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));
        processor2.Setup(x => x.ProcessMessageAsync(message))
            .Returns(() => new(true));

        var sut = CreateFoulBot();
        sut.AddFeature(processor1.Object);
        sut.AddFeature(processor2.Object);

        await sut.GracefulShutdownAsync();

        processor1.Verify(x => x.StopFeatureAsync());
        processor2.Verify(x => x.StopFeatureAsync());
    }

    #endregion

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldAddGeneratedMessageToContext_OnThePositiveFlow(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(true);

        var config = CreateDefaultConfig();
        var sut = CreateFoulBot(config);

        await sut.TriggerAsync(message);

        AssertContextNotified(responseMessage, config.BotName);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldNotAddGeneratedMessageToContext_WhenMessageIsBad(
        FoulMessage message,
        string responseMessage)
    {
        _aiClient.Setup(x => x.GetTextResponseAsync(It.IsAny<IEnumerable<FoulMessage>>())) // A hack because we don't test retries yet.
            .Returns(() => new(responseMessage));

        _messageFilter.Setup(x => x.IsGoodMessage(responseMessage))
            .Returns(false);

        var config = CreateDefaultConfig();
        var sut = CreateFoulBot(config);

        await sut.TriggerAsync(message);

        _chat.Verify(x => x.AddMessage(It.IsAny<FoulMessage>()), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldNotReply_WhenContextIsEmpty(
        FoulMessage message)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(() => null);

        var sut = CreateFoulBot();
        await sut.TriggerAsync(message);

        _botMessenger.Verify(x => x.SendTextMessageAsync(ChatId, It.IsAny<string>()), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldDelayTypingOrSending_ForDelayStrategyAmount(
        FoulMessage message)
    {
        var task = new Task(() => { });
        _delayStrategy.Setup(x => x.DelayAsync(Cts.Token))
            .Returns(() => new(task));
        var sut = CreateFoulBot();

        var resultTask = sut.TriggerAsync(message).AsTask();

        await WaitAsync(resultTask);
        Assert.False(resultTask.IsCompleted);

        task.Start();

        await resultTask;
        Assert.True(resultTask.IsCompleted);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldSimulateTyping_AndDisposeAtTheEnd(
        FoulMessage message,
        IList<FoulMessage> context,
        string responseMessage,
        IReplyImitator typingImitator)
    {
        var startedTyping = false;
        var finishedTyping = false;

        var responseMessageTask = new Task<string>(() => responseMessage);
        var typingImitatorFinishTask = new Task(() => finishedTyping = true);

        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessageTask));

        var mode = Fixture.Create<BotReplyMode>();
        _replyModePicker.Setup(x => x.GetBotReplyMode(context))
            .Returns(mode);

        _replyImitatorFactory.Setup(x => x.ImitateReplying(ChatId, mode))
            .Returns(typingImitator)
            .Callback(() => startedTyping = true);

        Mock.Get(typingImitator).Setup(x => x.FinishReplyingAsync(responseMessage))
            .Returns(() => new(typingImitatorFinishTask));

        var sut = CreateFoulBot();
        var resultTask = sut.TriggerAsync(message).AsTask();

        await WaitAsync(resultTask);
        Assert.True(startedTyping);
        Assert.False(resultTask.IsCompleted);

        responseMessageTask.Start();
        await WaitAsync(resultTask);
        Assert.False(resultTask.IsCompleted);

        typingImitatorFinishTask.Start();
        await resultTask;
        Assert.True(resultTask.IsCompleted);
        Assert.True(finishedTyping);
        Mock.Get(typingImitator).Verify(x => x.DisposeAsync());
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldInvokeBotFailedEvent_WhenExceptionHappens(
        FoulMessage message)
    {
        _botMessenger.Setup(x => x.SendTextMessageAsync(ChatId, It.IsAny<string>()))
            .Returns(() => new(Task.FromException(new InvalidOperationException())));

        var sut = CreateFoulBot();

        var fired = false;
        sut.Shutdown += (_, _) => fired = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sut.TriggerAsync(message));

        Assert.True(fired);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldOnlyAllowOneCallAtATime(FoulMessage message)
    {
        var task = new Task(() => { });
        _delayStrategy.Setup(x => x.DelayAsync(Cts.Token))
            .Returns(() => new(task));
        var sut = CreateFoulBot();

        var resultTask = sut.TriggerAsync(message).AsTask();

        await WaitAsync(resultTask);
        Assert.False(resultTask.IsCompleted);

        var anotherResultTask = sut.TriggerAsync(message).AsTask();
        await anotherResultTask;
        Assert.True(anotherResultTask.IsCompletedSuccessfully);
        _aiClient.Verify(x => x.GetTextResponseAsync(It.IsAny<IEnumerable<FoulMessage>>()), Times.Never);

        task.Start();

        await resultTask;
        Assert.True(resultTask.IsCompleted);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldImitateTyping_WhileCallingAi(
        FoulMessage message,
        string responseMessage,
        IList<FoulMessage> context,
        IReplyImitator imitator)
    {
        var aiTask = new Task<string>(() => { return responseMessage; });

        var order = 0;

        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _replyModePicker.Setup(x => x.GetBotReplyMode(context))
            .Returns(() => new(ReplyType.Text));

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(aiTask))
            .Callback(() => order = order == 1 ? 2 : 0);

        _replyImitatorFactory.Setup(x => x.ImitateReplying(ChatId, new(ReplyType.Text)))
            .Returns(imitator)
            .Callback(() => order = order == 0 ? 1 : 0);

        var sut = CreateFoulBot();

        var task = sut.TriggerAsync(message).AsTask();
        await WaitAsync();

        _aiClient.Verify(x => x.GetTextResponseAsync(context));
        Assert.False(task.IsCompleted);

        _replyImitatorFactory.Verify(x => x.ImitateReplying(ChatId, new(ReplyType.Text)));
        Mock.Get(imitator).Verify(x => x.FinishReplyingAsync(It.IsAny<string>()), Times.Never);

        aiTask.Start();
        await task;

        Mock.Get(imitator).Verify(x => x.FinishReplyingAsync(responseMessage));

        // Verify order.
        Assert.Equal(2, order);
    }

    [Theory, AutoMoqData]
    public async Task TriggerAsync_ShouldImitateRecordingVoice_WhileCallingAi(
        FoulMessage message,
        string responseMessage,
        IList<FoulMessage> context,
        IReplyImitator imitator,
        Stream voice)
    {
        var aiTask = new Task<Stream>(() => { return voice; });

        var order = 0;

        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _replyModePicker.Setup(x => x.GetBotReplyMode(context))
            .Returns(() => new(ReplyType.Voice));

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage))
            .Callback(() => order = order == 1 ? 2 : 0);

        _aiClient.Setup(x => x.GetAudioResponseAsync(responseMessage))
            .Returns(() => new(aiTask))
            .Callback(() => order = order == 2 ? 3 : 0);

        _replyImitatorFactory.Setup(x => x.ImitateReplying(ChatId, new(ReplyType.Voice)))
            .Returns(imitator)
            .Callback(() => order = order == 0 ? 1 : 0);

        var sut = CreateFoulBot();

        var task = sut.TriggerAsync(message).AsTask();
        await WaitAsync();

        _aiClient.Verify(x => x.GetTextResponseAsync(context));
        _aiClient.Verify(x => x.GetAudioResponseAsync(responseMessage));
        Assert.False(task.IsCompleted);

        _replyImitatorFactory.Verify(x => x.ImitateReplying(ChatId, new(ReplyType.Voice)));
        Mock.Get(imitator).Verify(x => x.FinishReplyingAsync(It.IsAny<string>()), Times.Never);

        aiTask.Start();
        await task;

        Mock.Get(imitator).Verify(x => x.FinishReplyingAsync(responseMessage));

        // Verify order.
        Assert.Equal(3, order);
    }

    [Theory]
    [InlineAutoMoqData(0)]
    [InlineAutoMoqData(200)]
    public async Task TriggerAsync_ShouldThrowAnException_WhenWrongReplyTypeReceived(
        ReplyType replyType,
        FoulMessage message,
        string responseMessage,
        IList<FoulMessage> context,
        IReplyImitator imitator)
    {
        _replyStrategy.Setup(x => x.GetContextForReplying(message))
            .Returns(context);

        _replyModePicker.Setup(x => x.GetBotReplyMode(context))
            .Returns(() => new(replyType));

        _aiClient.Setup(x => x.GetTextResponseAsync(context))
            .Returns(() => new(responseMessage));

        _replyImitatorFactory.Setup(x => x.ImitateReplying(ChatId, new(ReplyType.Text)))
            .Returns(imitator);

        var sut = CreateFoulBot();

        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await sut.TriggerAsync(message));
    }

    #endregion

    private void AssertContextNotified(string messageAddedToContext, string botName)
    {
        _chat.Verify(x => x.AddMessage(It.Is<FoulMessage>(
            message => message.MessageType == FoulMessageType.Bot
                && message.IsOriginallyBotMessage == true
                && message.Text == messageAddedToContext
                && message.SenderName == botName)));
    }

    #region PerformRequestAsync

    private void SetupPerformRequest(ChatParticipant requester, string request, string requestResponse)
    {
        var config = CreateDefaultConfig();
        Customize("config", config);

        _aiClient.Setup(x => x.GetCustomResponseAsync(It.Is<string>(
            r => r.Contains(config.Directive)
                && r.Contains($"You've been asked by {requester.Name} to do")
                && r.Contains($"{request}")
            )))
            .Returns(() => new(requestResponse));
    }

    [Theory, AutoMoqData]
    public async Task PerformRequestAsync_ShouldPerformRequest(
        ChatParticipant requester, string request, string requestMessage)
    {
        SetupPerformRequest(requester, request, requestMessage);

        var sut = CreateFoulBot();

        await sut.PerformRequestAsync(requester, request);

        _botMessenger.Verify(x => x.SendTextMessageAsync(_chat.Object.ChatId, requestMessage));
    }

    [Theory, AutoMoqData]
    public async Task PerformRequestAsync_ShouldNotAddTheRequestToContext(
        ChatParticipant requester, string request, string requestMessage)
    {
        SetupPerformRequest(requester, request, requestMessage);

        var sut = CreateFoulBot();

        await sut.PerformRequestAsync(requester, request);

        _chat.Verify(x => x.AddMessage(It.IsAny<FoulMessage>()), Times.Never);
    }

    #endregion

    private FoulBotConfiguration CreateDefaultConfig()
    {
        return Fixture.Build<FoulBotConfiguration>()
            .With(x => x.Stickers, [])
            .With(x => x.NotAnAssistant, true)
            .Create();
    }

    private FoulBot CreateFoulBot()
    {
        var config = CreateDefaultConfig();
        Customize("config", config);
        Customize("cts", Cts);

        return Fixture.Create<FoulBot>();
    }

    private FoulBot CreateFoulBot(FoulBotConfiguration config)
    {
        Customize("config", config);

        return Fixture.Create<FoulBot>();
    }
}
