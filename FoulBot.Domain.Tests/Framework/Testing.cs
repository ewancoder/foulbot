using Moq;

namespace FoulBot.Domain.Tests;

public abstract class Testing
{
    protected IFixture Fixture { get; } = AutoMoqDataAttribute.CreateFixture();

    protected TObject Create<TObject>() => Fixture.Create<TObject>();

    protected Mock<TObject> Freeze<TObject>() where TObject : class
        => Fixture.Freeze<Mock<TObject>>();
}
