namespace FoulBot.Domain.Tests;

public sealed class InlineAutoMoqDataAttribute : InlineAutoDataAttribute
{
    public InlineAutoMoqDataAttribute(params object[] objects)
        : base(new AutoMoqDataAttribute(), objects) { }
}

public sealed class AutoMoqDataAttribute : AutoDataAttribute
{
    public AutoMoqDataAttribute() : base(CreateFixture)
    {
    }

    public static IFixture CreateFixture()
    {
        return new Fixture()
            .Customize(new AutoMoqCustomization { ConfigureMembers = true });
    }
}
