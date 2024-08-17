namespace FoulBot.Domain.Tests;

public class AllowedChatsProviderTests
{
    [Theory, AutoMoqData]
    public async Task ShouldReadAndWrite(
        string fileName,
        FoulChatId chatId)
    {
        using var sut = new AllowedChatsProvider(fileName);

        Assert.False(await sut.IsAllowedChatAsync(chatId));

        await sut.AllowChatAsync(chatId);

        Assert.True(await sut.IsAllowedChatAsync(chatId));

        await sut.DisallowChatAsync(chatId);

        Assert.False(await sut.IsAllowedChatAsync(chatId));
    }

    [Theory, AutoMoqData]
    public async Task ShouldCreateFileIfNotExists(string fileName)
    {
        Assert.False(File.Exists(fileName));

        using var sut = new AllowedChatsProvider(fileName);
        await sut.IsAllowedChatAsync(new(fileName));

        Assert.True(File.Exists(fileName));
    }

    [Theory, AutoMoqData]
    public async Task ShouldWorkConcurrently(string fileName)
    {
        using var sut = new AllowedChatsProvider(fileName);
        var fixture = AutoMoqDataAttribute.CreateFixture();
        var ids = fixture.CreateMany<FoulChatId>(100);

        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = 10
        }, (id, _) => sut.AllowChatAsync(id));

        await Parallel.ForEachAsync(ids, new ParallelOptions
        {
            MaxDegreeOfParallelism = 10
        }, async (id, _) => Assert.True(await sut.IsAllowedChatAsync(id)));
    }

    [Theory, AutoMoqData]
    public async Task ShouldLoadExistingFile(string fileName, FoulChatId chatId)
    {
        using var sut = new AllowedChatsProvider(fileName);
        Assert.False(await sut.IsAllowedChatAsync(chatId));

        await sut.AllowChatAsync(chatId);
        Assert.True(await sut.IsAllowedChatAsync(chatId));

        using var sut2 = new AllowedChatsProvider(fileName);
        Assert.True(await sut2.IsAllowedChatAsync(chatId));
    }
}
