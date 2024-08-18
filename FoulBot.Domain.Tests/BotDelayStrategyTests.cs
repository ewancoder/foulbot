namespace FoulBot.Domain.Tests;

public class BotDelayStrategyTests : Testing<BotDelayStrategy>
{
    private readonly Mock<ISharedRandomGenerator> _random;
    private readonly BotDelayStrategy _sut;

    public BotDelayStrategyTests()
    {
        _random = Freeze<ISharedRandomGenerator>();
        _random.Setup(x => x.Generate(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(100_000_000);

        _sut = Fixture.Create<BotDelayStrategy>();
    }

    [Theory]
    [ClassData(typeof(BotDelayStrategyTheoryData))]
    public async Task DelayAsync_ShouldDelayForALongTime_VeryRarely(
        int randomNumberGenerated, int minimumDelay, int maximumDelay)
    {
        var correctDelay = 10_000;

        _random.Setup(x => x.Generate(1, 100)).Returns(randomNumberGenerated);
        _random.Setup(x => x.Generate(minimumDelay, maximumDelay)).Returns(correctDelay);

        var task = _sut.DelayAsync(Cts.Token).AsTask();
        await WaitAsync(task);
        Assert.False(task.IsCompleted);

        AdvanceTime(correctDelay - 1);
        await WaitAsync(task);
        Assert.False(task.IsCompleted);

        AdvanceTime(1);
        await WaitAsync(task);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DelayAsync_ShouldCancelDelay_WhenCancellationRequested()
    {
        var task = _sut.DelayAsync(Cts.Token).AsTask();
        await WaitAsync(task);
        Assert.False(task.IsCompleted);

        await Cts.CancelAsync();
        await WaitAsync(task);

        Assert.True(task.IsCanceled);
    }
}

public sealed class BotDelayStrategyTheoryData : TheoryData<int, int, int>
{
    /// <summary>
    /// When random between 1 and 100 is higher than 90, delay should be the highest.
    /// </summary>
    private const int SmallestChanceHigherThan = 90;

    /// <summary>
    /// When random between 1 and 100 is lower than 70, delay should be the smallest.
    /// </summary>
    private const int BiggestChanceLowerThan = 70;

    public BotDelayStrategyTheoryData()
    {
        foreach (var number in FromTo(SmallestChanceHigherThan + 1, 100))
        {
            Add(number, 5000, 20000);
        }

        foreach (var number in FromTo(BiggestChanceLowerThan + 1, SmallestChanceHigherThan))
        {
            Add(number, 1500, 5000);
        }

        foreach (var number in FromTo(1, BiggestChanceLowerThan))
        {
            Add(number, 200, 1200);
        }
    }

    /// <summary>
    /// If from is 10, and to is 15, we are generating [10, 11, 12, 13, 14, 15] 6 integers.
    /// 15 - 10 = 5, so we add 1 to include the last number.
    /// </summary>
    private static IEnumerable<int> FromTo(int from, int to)
        => Enumerable.Range(from, to - from + 1);
}
