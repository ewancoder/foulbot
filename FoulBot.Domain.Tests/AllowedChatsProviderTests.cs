namespace FoulBot.Domain.Tests;

public class AllowedChatsProviderTests : Testing<AllowedChatsProvider>
{
    private readonly string _fileName;
    private readonly AllowedChatsProvider _sut;

    public AllowedChatsProviderTests()
    {
        _fileName = Fixture.Create<string>();
        Fixture.Customizations.Add(new AllowedChatsProviderBuilder(_fileName));

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
    public async Task ShouldWorkConcurrently()
    {
        var ids = Fixture.CreateMany<FoulChatId>(100);

        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = 10
        }, (id, _) => _sut.AllowChatAsync(id));

        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = 10
        }, async (id, _) => Assert.True(await _sut.IsAllowedChatAsync(id)));
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

    public override void Dispose()
    {
        _sut.Dispose();
        base.Dispose();
    }
}

public sealed class AllowedChatsProviderBuilder(string fileName)
    : CustomParameterBuilder<AllowedChatsProvider, string>("fileName", fileName);
