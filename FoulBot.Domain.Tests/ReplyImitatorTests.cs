using FoulBot.Domain.Connections;

namespace FoulBot.Domain.Tests;

public class ReplyImitatorTests : Testing<ReplyImitator>
{
    private readonly FoulChatId _chatId;
    private readonly Mock<IBotMessenger> _messenger;
    private readonly Mock<ISharedRandomGenerator> _random;

    public ReplyImitatorTests()
    {
        _chatId = Fixture.Freeze<FoulChatId>();
        _messenger = Freeze<IBotMessenger>();
        _random = Freeze<ISharedRandomGenerator>();
    }

    private void SetupReplyType(ReplyType type)
    {
        Fixture.Register(() => new BotReplyMode(type));
    }

    [Fact]
    public async Task CreatingImitator_ShouldStartTheImitation()
    {
        SetupReplyType(ReplyType.Text);

        await using var sut = Fixture.Create<ReplyImitator>();

        _messenger.Verify(x => x.NotifyTyping(_chatId));
    }

    [Fact]
    public async Task ShouldNotifyTyping_WhenReplyModeIsTyping()
    {
        SetupReplyType(ReplyType.Voice);

        await using var sut = Fixture.Create<ReplyImitator>();

        _messenger.Verify(x => x.NotifyRecordingVoiceAsync(_chatId));
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Never);
    }

    [Theory, AutoMoqData]
    public async Task ShouldNotifyManyTimes_WithPauses(
        int waitTimeMs, int waitTimeMs2)
    {
        SetupReplyType(ReplyType.Text);

        _random.Setup(x => x.Generate(ReplyImitator.MinRandomWaitMs, ReplyImitator.MaxRandomWaitMs))
            .Returns(waitTimeMs);

        await using var sut = Fixture.Create<ReplyImitator>();

        _messenger.Verify(x => x.NotifyTyping(_chatId));

        _random.Setup(x => x.Generate(ReplyImitator.MinRandomWaitMs, ReplyImitator.MaxRandomWaitMs))
            .Returns(waitTimeMs2);

        TimeProvider.Advance(TimeSpan.FromMilliseconds(waitTimeMs - 1));
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Once);

        TimeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(2));

        TimeProvider.Advance(TimeSpan.FromMilliseconds(waitTimeMs2));
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(3));
    }

    [Theory]
    [InlineAutoData(false), InlineAutoData(true)]
    public async Task ShouldStopNotifying_WhenRequestedToStop(
        bool isRequestedManually,
        int waitTimeMs)
    {
        SetupReplyType(ReplyType.Text);

        _random.Setup(x => x.Generate(ReplyImitator.MinRandomWaitMs, ReplyImitator.MaxRandomWaitMs))
            .Returns(waitTimeMs);

        {
            await using var sut = Fixture.Create<ReplyImitator>();

            _messenger.Verify(x => x.NotifyTyping(_chatId));

            TimeProvider.Advance(TimeSpan.FromMilliseconds(waitTimeMs - 1));
            await WaitAsync();
            _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Once);

            if (isRequestedManually)
            {
                await sut.FinishReplyingAsync(string.Empty);

                await AssertStoppedImitationAsync(1);
                return;
            }
        }

        await AssertStoppedImitationAsync(1);
    }

    // Should type 1000 characters for 30 seconds (400 wpm): two Barbaras.
    [Fact]
    public async Task ShouldFinishTyping_WhenRequestedToStop_AndTextIsLarge()
    {
        SetupReplyType(ReplyType.Text);

        SetupNextRandom(300); // Waiting 300 ms first time.

        await using var sut = Fixture.Create<ReplyImitator>();

        _messenger.Verify(x => x.NotifyTyping(_chatId));

        TimeProvider.Advance(TimeSpan.FromMilliseconds(101)); // Canceling after 100 ms have passed.
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Once); // No more calls for now.

        SetupNextRandom(200);

        // This task will block until it's done typing, so we are not awaiting it.
        // Saying that the total text is 1000 characters (should be typed for 30 seconds).
        var task = sut.FinishReplyingAsync(new string('a', 1000)).AsTask();
        await WaitAsync();

        // It should cancel the wait, and start sending notifications to bot again.
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(2));

        // Still need to wait 29900.
        SetupNextRandom(29600);
        TimeProvider.Advance(TimeSpan.FromMilliseconds(201)); // Total 100 + 200 = 300.
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(3));

        // Still need to wait 29700.
        SetupNextRandom(100);
        TimeProvider.Advance(TimeSpan.FromMilliseconds(29601)); // Total 300 + 29600 = 29900.
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(4));

        Assert.False(task.IsCompleted);

        // This time we finish waiting for the whole 30000.
        TimeProvider.Advance(TimeSpan.FromMilliseconds(101));
        await WaitAsync(task);
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(4));

        Assert.True(task.IsCompleted);
        await AssertStoppedImitationAsync(4);
    }

    private void SetupNextRandom(int next)
    {
        _random.Setup(x => x.Generate(ReplyImitator.MinRandomWaitMs, ReplyImitator.MaxRandomWaitMs))
            .Returns(next);
    }

    private async Task AssertStoppedImitationAsync(int atIteration)
    {
        TimeProvider.Advance(TimeSpan.FromHours(1));
        await WaitAsync();
        _messenger.Verify(x => x.NotifyTyping(_chatId), Times.Exactly(atIteration));
    }
}
