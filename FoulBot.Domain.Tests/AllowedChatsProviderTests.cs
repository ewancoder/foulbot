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
}
