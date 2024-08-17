namespace FoulBot.Domain.Tests;

public abstract class Testing : IDisposable
{
    protected Testing()
    {
        Fixture.Register<TimeProvider>(() => TimeProvider);
    }

    protected IFixture Fixture { get; } = AutoMoqDataAttribute.CreateFixture();
    protected FakeTimeProvider TimeProvider { get; } = new FakeTimeProvider(startDateTime: DateTimeOffset.UtcNow);
    protected CancellationTokenSource Cts { get; } = new CancellationTokenSource();

    protected TObject Create<TObject>() => Fixture.Create<TObject>();

    protected Mock<TObject> Freeze<TObject>() where TObject : class
        => Fixture.Freeze<Mock<TObject>>();

    protected Task WaitAsync() => Task.Delay(10);
    protected void AdvanceTime(int milliseconds) => TimeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));

    public virtual void Dispose()
    {
        Cts.Dispose();
    }
}
