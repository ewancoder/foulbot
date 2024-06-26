namespace FoulBot.Domain.Tests;

public class AllowedChatsProviderTests
{
    [Theory, AutoMoqData]
    public void ShouldReadAndWrite(
        string fileName,
        FoulChatId chatId)
    {
        var sut = new AllowedChatsProvider(fileName);

        Assert.False(sut.IsAllowedChat(chatId));

        sut.AddAllowedChat(chatId);

        Assert.True(sut.IsAllowedChat(chatId));

        sut.RemoveAllowedChat(chatId);

        Assert.False(sut.IsAllowedChat(chatId));
    }
}
