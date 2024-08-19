namespace FoulBot.Domain.Tests;

public class BotReplyStrategyTests : Testing<BotReplyStrategy>
{
    public const string BotName = "botName";
    public const string Trigger = "Trigger";
    private readonly Mock<IFoulChat> _chat;

    public BotReplyStrategyTests()
    {
        _chat = Freeze<IFoulChat>();
        _chat.Setup(x => x.IsPrivateChat)
            .Returns(false);
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
            .With(x => x.KeyWords, [ Trigger ])
            .With(x => x.FoulBotId, foulBotId)
            .With(x => x.ContextSize, contextSize)
            .With(x => x.MaxContextSizeInCharacters, maxContextSizeInCharacters)
            .Create();

        _chat.Setup(x => x.GetContextSnapshot())
            .Returns(context);

        Customize("config", config);

        var sut = Fixture.Create<BotReplyStrategy>();

        var contextForReplying = sut.GetContextForReplying(currentMessage);

        if (contextForReplying == null)
        {
            Assert.Null(result);
            return;
        }

        // Assert first message is directive.
        var directive = contextForReplying.First();
        Assert.Equal(FoulMessageType.System, directive.MessageType);
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
            Message(senderName: BotReplyStrategyTests.BotName),
            null,
            100, 100);

        // Default condition: all messages are processed.
        var messages = GenerateMessages();

        var triggered = messages
            .Where(x => x.Text.Contains(BotReplyStrategyTests.Trigger))
            .OrderByDescending(x => x.Date)
            .ToList();

        var nonTriggered = messages
            .Where(x => !x.Text.Contains(BotReplyStrategyTests.Trigger))
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
            .With(x => x.MessageType, FoulMessageType.User)
            .CreateMany(20)
            .Concat(_fixture
                .Build<FoulMessage>()
                .With(x => x.Text, _fixture.Create<string>())
                .With(x => x.MessageType, FoulMessageType.User)
                .CreateMany(40))
            .ToList();
    }

    private FoulMessage Message(string senderName = "default")
    {
        return _fixture.Build<FoulMessage>()
            .With(x => x.Text, GenerateTriggeredText())
            .With(x => x.MessageType, FoulMessageType.User)
            .With(x => x.SenderName, senderName)
            .Create();
    }

    private FoulMessage Message(int hours, bool isTriggered)
    {
        return _fixture.Build<FoulMessage>()
            .With(x => x.Date, DateTime.MinValue + TimeSpan.FromHours(hours))
            .With(x => x.MessageType, FoulMessageType.User)
            .With(x => x.Text, isTriggered ? BotReplyStrategyTests.Trigger : _fixture.Create<string>())
            .Create();
    }

    private string GenerateTriggeredText()
    {
        return $"{_fixture.Create<string>()} {BotReplyStrategyTests.Trigger} {_fixture.Create<string>()}";
    }
}
