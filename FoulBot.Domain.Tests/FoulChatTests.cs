namespace FoulBot.Domain.Tests;

public class FoulChatTests : Testing<FoulChat>
{
    [Fact]
    public void GetContextSnapshot_ShouldReturnEmptyCollection_FromStart()
    {
        var sut = CreateFoulChat();

        var snapshot = sut.GetContextSnapshot();
        Assert.Empty(snapshot);
    }

    [Theory, AutoMoqData]
    public void GetContextSnapshot_ShouldReturnContext_OrderedByDate(
        IEnumerable<FoulMessage> messages)
    {
        messages = messages.OrderByDescending(x => x.Date);

        var sut = CreateFoulChat(messages);

        var snapshot = sut.GetContextSnapshot();

        Assert.Equal(messages.OrderBy(x => x.Date), snapshot);
    }

    [Theory, AutoMoqData]
    public async void GetContextSnapshot_ShouldWorkConcurrently()
    {
        var sut = CreateFoulChat();

        var t1 = Task.Run(() =>
        {
            Parallel.For(1, 1000, index => sut.AddMessage(Fixture.Create<FoulMessage>()));
        });

        var t2 = Task.Run(() =>
        {
            Parallel.For(1, 1000, index => sut.GetContextSnapshot());
        });

        await Task.WhenAll(t1, t2);
    }

    private FoulChat CreateFoulChat(IEnumerable<FoulMessage> context)
    {
        var chat = Fixture.Create<FoulChat>();
        foreach (var message in context)
            chat.AddMessage(message);

        return chat;
    }

    private FoulChat CreateFoulChat()
    {
        return Fixture.Create<FoulChat>();
    }
}
