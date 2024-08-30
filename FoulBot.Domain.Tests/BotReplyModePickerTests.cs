namespace FoulBot.Domain.Tests;

public class BotReplyModePickerTests : Testing<BotReplyModePicker>
{
    [Theory, AutoMoqData]
    public void ShouldReturnTextType_WhenNoVoiceConfigured(
        IList<FoulMessage> context,
        FoulBotConfiguration config)
    {
        config = config.WithVoiceBetween(0);

        Customize("config", config);

        var sut = Fixture.Create<BotReplyModePicker>();

        var mode = sut.GetBotReplyMode(context);

        Assert.Equal(ReplyType.Text, mode.Type);
    }

    [Theory]
    [InlineAutoMoqData(1, "1,3,5,7,9,11,13,15,17,19")]
    [InlineAutoMoqData(2, "2,5,8,11,14,17,20")]
    [InlineAutoMoqData(7, "7,15")]
    public void ShouldReturnVoiceMessage_EveryNMessages(
        int voiceBetween, string voicesOnIterations,
        IList<FoulMessage> context,
        FoulBotConfiguration config)
    {
        var intVoices = voicesOnIterations.Split(',').Select(int.Parse).ToArray();
        config = config.WithVoiceBetween(voiceBetween);
        Customize("config", config);

        var sut = Fixture.Create<BotReplyModePicker>();

        var i = 0;
        bool ShouldBeVoice() => intVoices.Contains(i);

        while (i <= 20)
        {
            var mode = sut.GetBotReplyMode(context);
            var expectedType = ShouldBeVoice() ? ReplyType.Voice : ReplyType.Text;

            Assert.Equal(expectedType, mode.Type);

            i++;
        }
    }
}
