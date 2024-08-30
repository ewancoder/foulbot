using AutoFixture.Dsl;

namespace FoulBot.Domain.Tests;

public class NotABotMessage : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Customize<FoulMessage>(
            composer => composer
                .With(p => p.IsOriginallyBotMessage, false)
                .With(p => p.ForceReply, false));
    }
}

// TODO: Currently tests are testing BotReplyStrategy together with ContextReducer.
// Split these tests.
public class BotReplyStrategyTests : Testing<BotReplyStrategy>
{
    public const string BotName = "botName";
    private readonly Mock<IFoulChat> _chat;
    private ContextReducer _contextReducer;

    public BotReplyStrategyTests()
    {
        _chat = Freeze<IFoulChat>();
        _chat.Setup(x => x.IsPrivateChat)
            .Returns(false);

        _contextReducer = Fixture.Create<ContextReducer>();
        Fixture.Register<IContextReducer>(() => _contextReducer);

        Fixture.Customize(new NotABotMessage());
    }

    private IPostprocessComposer<FoulMessage> BuildMessage()
        => Fixture.Build<FoulMessage>()
            .With(x => x.IsOriginallyBotMessage, false)
            .With(x => x.ForceReply, false);

    private IList<FoulMessage> ConfigureWithContext(IList<FoulMessage>? messages = null)
    {
        var context = messages ?? Fixture.CreateMany<FoulMessage>()
            .OrderBy(x => x.Date)
            .ToList();

        _chat.Setup(x => x.GetContextSnapshot())
            .Returns(context);

        return context;
    }

    private FoulBotConfiguration ConfigureBot(string[]? triggers = null, string[]? keywords = null)
    {
        var config = Fixture.Build<FoulBotConfiguration>()
            .With(x => x.Triggers, triggers ?? [])
            .With(x => x.KeyWords, keywords ?? [])
            .Create();

        CustomizeConfig(config);

        return config;
    }

    private void CustomizeConfig(FoulBotConfiguration config)
    {
        Customize("config", config);
        _contextReducer = new ContextReducer(config);
    }

    private FoulMessage GenerateMessageWithPart(string part)
    {
        return BuildMessage()
            .With(x => x.Text, $"{Fixture.Create<string>()}{part}{Fixture.Create<string>()}")
            .Create();
    }

    // Always send messages to private chats. But only unprocessed ones.
    [Fact]
    public void ShouldAlwaysProduceResults_WhenChatIsPrivate()
    {
        var context = ConfigureWithContext();

        _chat.Setup(x => x.IsPrivateChat)
            .Returns(true);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        var message = Fixture.Create<FoulMessage>();
        context.Add(message);

        result = sut.GetContextForReplying(message);
        Assert.NotNull(result);
    }

    // Do not process already processed messages even in private chats.
    [Fact]
    public void ShouldNotProduceResults_WhenChatIsPrivate_AndMessageHasAlreadyBeenHandled()
    {
        var context = ConfigureWithContext();

        _chat.Setup(x => x.IsPrivateChat)
            .Returns(true);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    // Always produce results - even when messages are processed - when forced.
    [Fact]
    public void ShouldAlwaysProduceResults_EvenWhenMessageHasAlreadyBeenHandled_WhenForced()
    {
        var context = ConfigureWithContext();

        _chat.Setup(x => x.IsPrivateChat)
            .Returns(true);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        context[^1] = context[^1] with { ForceReply = true };
        result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);
        result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);
    }

    // When message is a direct reply to the bot - always process it.
    [Fact]
    public void ShouldAlwaysProduceResults_WhenItsAReplyToTheBot()
    {
        var config = ConfigureBot();

        var messages = BuildMessage()
            .With(x => x.ReplyTo, config.BotId)
            .Create();

        var context = ConfigureWithContext([ messages ]);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        var message = Fixture.Create<FoulMessage>();
        context.Add(message);

        result = sut.GetContextForReplying(message);
        Assert.Null(result);

        message = BuildMessage()
            .With(x => x.ReplyTo, config.BotId)
            .Create();
        context.Add(message);

        result = sut.GetContextForReplying(message);
        Assert.NotNull(result);
    }

    // When message is a direct reply to the bot but it has already been processed - do not reply.
    [Fact]
    public void ShouldNotProduceResults_WhenItsAReplyToTheBot_AndMessageHasAlreadyBeenHandled()
    {
        var config = ConfigureBot();

        var messages = BuildMessage()
            .With(x => x.ReplyTo, config.BotId)
            .CreateMany()
            .ToList();

        var context = ConfigureWithContext(messages);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        result = sut.GetContextForReplying(context[1]);
        Assert.Null(result);
    }

    // When there are unprocessed messages that have keyword in them - process them.
    [Theory, AutoMoqData]
    public void ShouldProduceResults_WhenTriggeredByKeyWord(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0]),
            Fixture.Create<FoulMessage>()
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[1]);
        Assert.NotNull(result);
    }

    // When multiple messages with keywords in them come in short succession - skip the second one.
    [Theory, AutoMoqData]
    public void ShouldNotProduceResults_WhenTriggeredByKeyWord_MultipleTimes_WithoutWaiting(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0])
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);

        context.Add(GenerateMessageWithPart(keywords[1]));
        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    // When enough time has passed between multiple messages with keywords - process both.
    [Theory, AutoMoqData]
    public void ShouldProduceResults_WhenTriggeredByKeyWord_MultipleTimes_AfterMinimumTimeBetweenMessagesHasPassed(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0])
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);

        context.Add(GenerateMessageWithPart(keywords[1]));
        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages);
        result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);
    }

    // When enough time has passed between multiple messages with keywords, but latest message has already been processed - skip.
    [Theory, AutoMoqData]
    public void ShouldNotProduceResults_WhenTriggeredByKeyWord_MultipleTimes_WithWaiting_AndMessageHasAlreadyBeenProcessed(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0])
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages);
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    // When new messages appear in context while debounce is in effect - skip them.
    [Theory, AutoMoqData]
    public void ShouldNotProduceResults_WhenTriggeredByKeyWord_AfterWaiting_ButThatMessageHasBeenAddedWhileWaiting(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0])
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[0]);
        Assert.NotNull(result);

        context.Add(Fixture.Create<FoulMessage>());
        sut.GetContextForReplying(context[0]);

        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages);
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    // When second message comes after waiting - but it was already present when we processed the first one - do not process it again.
    [Theory, AutoMoqData]
    public void ShouldNotProcessOldMessage_WhenTriggeredSecondTimeAfterWaiting(string[] keywords)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(keywords[0]),
            GenerateMessageWithPart(keywords[1])
        ]);

        ConfigureBot(keywords: keywords);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[1]);
        Assert.NotNull(result);

        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[2]);
        Assert.Null(result);
    }

    // When there are unprocessed messages that have trigger in them with spaces - process them.
    [Theory, AutoMoqData]
    public void ShouldProduceResults_WhenTriggeredByTrigger(string[] triggers)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart($" {triggers[0]} "),
            Fixture.Create<FoulMessage>()
        ]);

        ConfigureBot(keywords: triggers);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[1]);
        Assert.NotNull(result);
    }

    // When there are unprocessed messages that have trigger in them without spaces - skip them.
    [Theory, AutoMoqData]
    public void ShouldNotProduceResults_WhenTriggeredByTrigger_AndItsNotSpaced(string[] triggers)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart(triggers[0]),
            Fixture.Create<FoulMessage>()
        ]);

        ConfigureBot(keywords: triggers);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[1]);
        Assert.NotNull(result);
    }

    // When multiple messages with triggers in them come in short succession - process the second one too.
    [Theory, AutoMoqData]
    public void ShouldProduceResults_WhenTriggeredByTrigger_MultipleTimes_WithoutWaiting(string[] triggers)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart($" {triggers[0]} ")
        ]);

        ConfigureBot(triggers: triggers);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);

        context.Add(GenerateMessageWithPart($" {triggers[1]} "));
        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);
    }

    // When multiple messages with triggers in them come in short succession - process the second one too.
    [Theory, AutoMoqData]
    public void ShouldNotProduceResults_WhenTriggeredByTrigger_MultipleTimes_WithoutWaiting_ButSecondOneNotSpaced(string[] triggers)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart($" {triggers[0]} ")
        ]);

        ConfigureBot(triggers: triggers);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);

        context.Add(GenerateMessageWithPart(triggers[1]));
        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    // When second message comes after waiting - but it was already present when we processed the first one - do not process it again.
    [Theory, AutoMoqData]
    public void ShouldNotProcessOldMessage_WhenTriggeredSecondTimeAfterWaiting_ByTriggers(string[] triggers)
    {
        var context = ConfigureWithContext([
            Fixture.Create<FoulMessage>(),
            GenerateMessageWithPart($" {triggers[0]} "),
            GenerateMessageWithPart($" {triggers[1]} ")
        ]);

        ConfigureBot(triggers: triggers);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[1]);
        Assert.NotNull(result);

        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[2]);
        Assert.Null(result);
    }

    [Theory, AutoMoqData]
    public void ShouldProcess_WhenCurrentMessageIsAReplyToTheBot(
        FoulBotConfiguration config)
    {
        Customize("config", config);

        var sut = Fixture.Create<BotReplyStrategy>();

        var response = sut.GetContextForReplying(BuildMessage()
            .With(x => x.ReplyTo, config.BotId)
            .Create());

        Assert.NotNull(response);
    }

    [Theory, AutoMoqData]
    public void ShouldNotProcess_WhenCurrentMessageIsAMessageFromTheBot(
        FoulBotConfiguration config)
    {
        Customize("config", config);

        var sut = Fixture.Create<BotReplyStrategy>();

        var response = sut.GetContextForReplying(BuildMessage()
            .With(x => x.Sender, () => new(config.BotName))
            .With(x => x.IsOriginallyBotMessage, true)
            .Create());

        Assert.Null(response);
    }

    [Theory, AutoMoqData]
    public void ShouldProcess_WhenCurrentMessageIsFromAUserWithTheSameNameAsABot(
        FoulBotConfiguration config)
    {
        Customize("config", config);

        var sut = Fixture.Create<BotReplyStrategy>();

        var response = sut.GetContextForReplying(BuildMessage()
            .With(x => x.Sender, () => new(config.BotName))
            .With(x => x.IsOriginallyBotMessage, false)
            .With(x => x.ReplyTo, config.BotId) // To make the message trigger a reply.
            .Create());

        Assert.NotNull(response);
    }

    [Fact]
    public void GetContextForReplying_ShouldConvertBotMessagesToUserMessages()
    {
        var messages = BuildMessage()
            .With(x => x.SenderType, FoulMessageSenderType.Bot)
            .With(x => x.Text, "trigger")
            .CreateMany()
            .ToList();

        ConfigureWithContext(messages);
        ConfigureBot(keywords: ["trigger"]);

        var sut = Fixture.Create<BotReplyStrategy>();

        var response = sut.GetContextForReplying(messages[^1]);
        // Skipping system directive.
        Assert.True(response!.Skip(1).All(message => message.SenderType == FoulMessageSenderType.User));
    }

    [Theory]
    [InlineData("some_Trigger")]
    [InlineData("trigger")]
    [InlineData("TRIGGERabc")]
    [InlineData("tRiGGer")]
    [InlineData("  abtRiGGer")]
    [InlineData("  tRiGGer  ")]
    public void GetContextForReplying_ShouldReturnResult_WhenEnoughTimePassedBetweenKeywords_WorksAnyCase(
        string trigger)
    {
        var context = BuildMessage()
            .With(x => x.Text, "some_trigger_some")
            .CreateMany()
            .OrderBy(x => x.Date)
            .ToList();

        void AddMessages(string keyword)
        {
            context.Add(BuildMessage()
                .With(x => x.Text, $"{Fixture.Create<string>()}{keyword}{Fixture.Create<string>()}")
                .Create());
        }

        ConfigureWithContext(context);
        ConfigureBot(keywords: ["trigGer"]);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);

        Assert.NotNull(result);

        AddMessages(trigger);
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);

        AddMessages(trigger);
        TimeProvider.Advance(BotReplyStrategy.MinimumTimeBetweenMessages - TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);

        AddMessages(trigger);
        TimeProvider.Advance(TimeSpan.FromSeconds(1));
        result = sut.GetContextForReplying(context[^1]);
        Assert.NotNull(result);

        AddMessages(trigger);
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("mandatorytrigger", true)]
    [InlineData("hi mandatorytrigger bye", true)]
    [InlineData("mandatorytrigger_with trigger", false)]
    public void GetContextForReplying_ShouldReturnResult_EvenWhenNotEnoughTimePassedBetweenTriggers_IfMandatoryTrigger(
        string trigger, bool shouldReply)
    {
        var context = BuildMessage()
            .With(x => x.Text, $"something {trigger} something")
            .CreateMany()
            .OrderBy(x => x.Date)
            .ToList();

        void AddMessages(string trigger)
        {
            context.Add(BuildMessage()
                .With(x => x.Text, $"{Fixture.Create<string>()} {trigger} {Fixture.Create<string>()}")
                .Create());
        }

        ConfigureWithContext(context);
        ConfigureBot(triggers: ["mandatorytrigger"]);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);

        if (shouldReply)
            Assert.NotNull(result);
        else
            Assert.Null(result);

        AddMessages(trigger);
        result = sut.GetContextForReplying(context[^1]);

        if (shouldReply)
            Assert.NotNull(result);
        else
            Assert.Null(result);

        // Adding regular message after a hard-triggered one should not trigger a bot.
        AddMessages("something");
        result = sut.GetContextForReplying(context[^1]);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Trigger", false)]
    [InlineData("TRIGGER", false)]
    [InlineData("  tRiGGer  ", false)]
    [InlineData("atoeuMandatoryTriggerthoeu", false)]
    [InlineData("thoeuMandatoryTrigger oteuh", false)]
    [InlineData("oteuh MandatoryTrigger oetuh", true)]
    [InlineData("otaehu MandatoryTrigger", true)]
    [InlineData("mandatorytRIGGER aoteuh", true)]
    [InlineData("mandatorytRIGGER", true)]
    [InlineData("mandatorytRIGGERsomething", false)]
    [InlineData("somethingmandatorytrigger", false)]
    public void GetContextForReplying_ShouldReturnResult_WhenMandatoryTriggerIsFound_WorksAnyCase_ButWithSpaces(
        string trigger, bool returns)
    {
        var context = BuildMessage()
            .With(x => x.Text, trigger)
            .CreateMany()
            .OrderBy(x => x.Date)
            .ToList();

        ConfigureWithContext(context);
        ConfigureBot(triggers: ["mandatoRYTRIgger"]);

        var sut = Fixture.Create<BotReplyStrategy>();

        var result = sut.GetContextForReplying(context[^1]);

        if (returns)
            Assert.NotNull(result);
        else
            Assert.Null(result);
    }

    [Theory]
    [ClassData(typeof(BotReplyStrategyTheoryData))]
    public void GetContextForReplying_ShouldProduceResults(
        IList<FoulMessage> context,
        FoulMessage currentMessage,
        IList<FoulMessage>? result,
        int contextSize, int maxContextSizeInCharacters)
    {
        // Context is always ordered by date.
        context = context.OrderBy(x => x.Date).ToList();

        var foulBotId = Fixture.Build<FoulBotId>()
            .With(x => x.BotName, BotName)
            .Create();

        var config = Fixture.Build<FoulBotConfiguration>()
            .With(x => x.KeyWords, [ "trigger" ])
            .With(x => x.Triggers, [ "mandatory" ])
            .With(x => x.FoulBotId, foulBotId)
            .With(x => x.ContextSize, contextSize)
            .With(x => x.MaxContextSizeInCharacters, maxContextSizeInCharacters)
            .Create();

        _chat.Setup(x => x.GetContextSnapshot())
            .Returns(context);

        CustomizeConfig(config);

        var sut = Fixture.Create<BotReplyStrategy>();

        var contextForReplying = sut.GetContextForReplying(currentMessage);

        if (contextForReplying == null)
        {
            Assert.Null(result);
            return;
        }

        // Assert first message is directive.
        var directive = contextForReplying[0];
        Assert.Equal(FoulMessageSenderType.System, directive.SenderType);
        Assert.Equal("System", directive.SenderName);
        Assert.Equal(config.Directive, directive.Text);
        Assert.False(directive.IsOriginallyBotMessage);

        // Result should be ordered by date.
        Assert.Equal(contextForReplying.OrderBy(x => x.Date), contextForReplying);

        // Should contain all expected messages.
        var others = contextForReplying.Skip(1).ToList();
        foreach (var r in result!)
        {
            Assert.Contains(r, others);
        }

        // Count should be less or equal than context size.
        Assert.True(others.Count <= config.ContextSize);

        // Total characters count should be less or equal than character context size.
        Assert.True(others.Sum(x => x.Text.Length) <= config.MaxContextSizeInCharacters * 2);
    }
}

public sealed class BotReplyStrategyTheoryData : TheoryData<List<FoulMessage>, FoulMessage, List<FoulMessage>?, int, int>
{
    private readonly IFixture _fixture = AutoMoqDataAttribute.CreateFixture();

    public BotReplyStrategyTheoryData()
    {
        // When bot is the sender - do not reply.
        Add(
            GenerateMessages(),
            Message(senderName: BotReplyStrategyTests.BotName, true),
            null,
            100, 100);

        // Default condition: all messages are processed.
        var messages = GenerateMessages();

        var triggered = messages
            .Where(x => x.Text.Contains("trigger"))
            .OrderByDescending(x => x.Date)
            .ToList();

        var nonTriggered = messages
            .Where(x => !x.Text.Contains("trigger"))
            .OrderByDescending(x => x.Date)
            .ToList();

        Add(messages,
            Message(),
            [
                triggered[0],
                nonTriggered[0]
            ],
            8, 100_000);

        Add(messages,
            Message(),
            [ triggered[0], nonTriggered[0] ],
            100_000, 100);

        List<FoulMessage> msg = [
            Message(0, true),
            Message(1, true),
            Message(2, true),
            Message(3, true),

            Message(4, false),
            Message(5, false),
            Message(6, false),
            Message(7, false)
        ];

        Add(msg, Message(8, false), [
            msg[1], msg[2], msg[3], // Priority to triggered messages.
            msg[6], msg[7]
        ],
        5, 100_000);
    }

    private List<FoulMessage> GenerateMessages()
    {
        return _fixture
            .Build<FoulMessage>()
            .With(x => x.Text, GenerateTriggeredText())
            .With(x => x.SenderType, FoulMessageSenderType.User)
            .With(x => x.IsOriginallyBotMessage, false)
            .With(x => x.ForceReply, false)
            .CreateMany(20)
            .Concat(_fixture
                .Build<FoulMessage>()
                .With(x => x.Text, _fixture.Create<string>())
                .With(x => x.SenderType, FoulMessageSenderType.User)
                .With(x => x.IsOriginallyBotMessage, false)
                .With(x => x.ForceReply, false)
                .CreateMany(40))
            .ToList();
    }

    private FoulMessage Message(string senderName = "default", bool isOriginallyBotMessage = false)
    {
        return _fixture.Build<FoulMessage>()
            .With(x => x.Text, GenerateTriggeredText())
            .With(x => x.SenderType, FoulMessageSenderType.User)
            .With(x => x.Sender, () => new(senderName))
            .With(x => x.IsOriginallyBotMessage, isOriginallyBotMessage)
            .With(x => x.ForceReply, false)
            .Create();
    }

    private FoulMessage Message(int hours, bool isTriggered)
    {
        return _fixture.Build<FoulMessage>()
            .With(x => x.Date, DateTime.MinValue + TimeSpan.FromHours(hours))
            .With(x => x.SenderType, FoulMessageSenderType.User)
            .With(x => x.Text, isTriggered ? "trigger": _fixture.Create<string>())
            .With(x => x.IsOriginallyBotMessage, false)
            .With(x => x.ForceReply, false)
            .Create();
    }

    private string GenerateTriggeredText()
    {
        return $"{_fixture.Create<string>()}{"trigger"}{_fixture.Create<string>()}";
    }
}
