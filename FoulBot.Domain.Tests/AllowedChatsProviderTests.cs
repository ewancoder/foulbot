namespace FoulBot.Domain.Tests;

public class AllowedChatsProviderTests : Testing, IDisposable
{
    private readonly string _fileName;
    private readonly AllowedChatsProvider _sut;

    public AllowedChatsProviderTests()
    {
        _fileName = _fixture.Create<string>();
        _fixture.Customizations.Add(new AllowedChatsProviderBuilder(_fileName));

        _sut = _fixture.Create<AllowedChatsProvider>();
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

    [Theory, AutoMoqData]
    public async Task ShouldCreateFileIfNotExists()
    {
        Assert.False(File.Exists(_fileName));

        await _sut.IsAllowedChatAsync(new(_fileName));

        Assert.True(File.Exists(_fileName));
    }

    [Theory, AutoMoqData]
    public async Task ShouldWorkConcurrently()
    {
        var fixture = AutoMoqDataAttribute.CreateFixture();
        var ids = fixture.CreateMany<FoulChatId>(100);

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

        using var _sut2 = _fixture.Create<AllowedChatsProvider>();
        Assert.True(await _sut2.IsAllowedChatAsync(chatId));
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}

public sealed class AllowedChatsProviderBuilder(string fileName)
    : CustomParameterBuilder<AllowedChatsProvider, string>("fileName", fileName);
