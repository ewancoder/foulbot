namespace FoulBot.Domain.Tests;

public class AllowedChatsProviderTests : Testing<AllowedChatsProvider>
{
    private readonly string _fileName;
    private readonly AllowedChatsProvider _sut;

    public AllowedChatsProviderTests()
    {
        _fileName = Fixture.Create<string>();
        Customize("fileName", _fileName);

        _sut = Fixture.Create<AllowedChatsProvider>();
    }

    [Theory, AutoMoqData]
    public async Task ShouldReadAndWrite(FoulChatId chatId)
    {
        Assert.False(await _sut.IsAllowedChatAsync(chatId));

        await _sut.AllowChatAsync(chatId);

        Assert.True(await _sut.IsAllowedChatAsync(chatId));

        await _sut.DisallowChatAsync(chatId);

        Assert.False(await _sut.IsAllowedChatAsync(chatId));
    }

    [Fact]
    public async Task ShouldCreateFileIfNotExists()
    {
        Assert.False(File.Exists(_fileName));

        await _sut.IsAllowedChatAsync(new(_fileName));

        Assert.True(File.Exists(_fileName));
    }

    [Fact]
    [Trait(Category, Concurrency)]
    public async Task ShouldWorkConcurrently()
    {
        var ids = Fixture.CreateMany<FoulChatId>(100);

        await Parallel.ForEachAsync(
            ids, ParallelOptions, (id, _) => _sut.AllowChatAsync(id));

        await Parallel.ForEachAsync(
            ids, ParallelOptions, async (id, _) => Assert.True(await _sut.IsAllowedChatAsync(id)));
    }

    [Theory, AutoMoqData]
    public async Task ShouldLoadExistingFile(FoulChatId chatId)
    {
        Assert.False(await _sut.IsAllowedChatAsync(chatId));

        await _sut.AllowChatAsync(chatId);
        Assert.True(await _sut.IsAllowedChatAsync(chatId));

        using var _sut2 = Fixture.Create<AllowedChatsProvider>();
        Assert.True(await _sut2.IsAllowedChatAsync(chatId));
    }

    [Fact]
    public async Task ShouldAllowPublicChat_ForAllBots()
    {
        var chatId = Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, () => null)
            .Create();

        await _sut.AllowChatAsync(chatId);

        Assert.True(await _sut.IsAllowedChatAsync(chatId));

        var anotherBotChatId = chatId with { };

        Assert.True(await _sut.IsAllowedChatAsync(anotherBotChatId));
    }

    [Fact]
    public async Task ShouldAllowPrivateChat_ForSpecificBotOnly()
    {
        var chatId = Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, Fixture.Create<FoulBotId>())
            .Create();

        await _sut.AllowChatAsync(chatId);

        Assert.True(await _sut.IsAllowedChatAsync(chatId));

        var anotherBotSameChatId = chatId with { FoulBotId = Fixture.Create<FoulBotId>() };

        Assert.False(await _sut.IsAllowedChatAsync(anotherBotSameChatId));
    }

    [Fact]
    public async Task ShouldGetAllAllowedChats()
    {
        var chats = new List<FoulChatId>
        {
            Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, () => null)
            .Create(),
            Fixture.Build<FoulChatId>()
            .With(x => x.FoulBotId, () => Fixture.Create<FoulBotId>())
            .Create()
        };

        foreach (var chat in chats)
        {
            await _sut.AllowChatAsync(chat);
        }

        var allowedChats = await _sut.GetAllAllowedChatsAsync();

        Assert.Equal(
            chats.Select(x => x.FoulBotId?.BotId).Order(),
            allowedChats.Select(x => x.FoulBotId?.BotId).Order());

        Assert.Equal(
            chats.Select(x => x.Value).Order(),
            allowedChats.Select(x => x.Value).Order());
    }

    public override void Dispose()
    {
        _sut.Dispose();
        base.Dispose();
    }
}
