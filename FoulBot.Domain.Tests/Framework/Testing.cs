namespace FoulBot.Domain.Tests;

public abstract class Testing<TSut> : IDisposable
{
    protected Testing()
    {
        Fixture.Register<TimeProvider>(() => TimeProvider);
    }

    public const string Category = "Category";
    public const string Concurrency = "Concurrency";

    protected IFixture Fixture { get; } = AutoMoqDataAttribute.CreateFixture();
    protected FakeTimeProvider TimeProvider { get; } = new FakeTimeProvider(startDateTime: DateTimeOffset.UtcNow);
    protected CancellationTokenSource Cts { get; } = new CancellationTokenSource();

    protected TObject Create<TObject>() => Fixture.Create<TObject>();

    protected Mock<TObject> Freeze<TObject>() where TObject : class
        => Fixture.Freeze<Mock<TObject>>();

    protected Task WaitAsync() => Task.Delay(5);
    protected Task WaitAsync(Task task) => Task.WhenAny(task, WaitAsync());
    protected void AdvanceTime(int milliseconds) => TimeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));

    protected void Customize<TParameter>(string parameterName, TParameter value)
    {
        Fixture.Customizations.Add(
            new CustomParameterBuilder<TSut, TParameter>(
                parameterName, value));
    }

    protected ParallelOptions ParallelOptions => new()
    {
        MaxDegreeOfParallelism = 10
    };

    public virtual void Dispose()
    {
        Cts.Dispose();
    }
}
