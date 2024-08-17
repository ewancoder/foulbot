namespace FoulBot.Domain.Tests;

public abstract class Testing
{
    protected IFixture _fixture = AutoMoqDataAttribute.CreateFixture();
}
