namespace FoulBot.Domain;

public readonly record struct BotReplyMode(
    ReplyType Type);

public enum ReplyType
{
    Text = 1,
    Voice
}

public interface IBotReplyModePicker
{
    BotReplyMode GetBotReplyMode(IList<FoulMessage> context);
}

public sealed class BotReplyModePicker : IBotReplyModePicker
{
    private readonly FoulBotConfiguration _config;
    private int _voiceCounter;

    public BotReplyModePicker(FoulBotConfiguration config)
    {
        _config = config;
    }

    public BotReplyMode GetBotReplyMode(IList<FoulMessage> context)
    {
        if (_config.MessagesBetweenVoice > 0)
        {
            _voiceCounter++;

            if (_voiceCounter > _config.MessagesBetweenVoice)
            {
                _voiceCounter = 0;
                return new(ReplyType.Voice);
            }
        }

        return new(ReplyType.Text);
    }
}
