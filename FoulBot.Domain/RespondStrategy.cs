using System;
using System.Linq;

namespace FoulBot.Domain;

public interface IRespondStrategy
{
    string? GetReasonForResponding(FoulMessage message);
}

public sealed class RespondStrategy : IRespondStrategy
{
    private readonly FoulBotConfiguration _config;
    private readonly bool _isPrivateChat;

    public RespondStrategy(
        FoulBotConfiguration config,
        bool isPrivateChat)
    {
        _config = config;
        _isPrivateChat = isPrivateChat;
    }

    /// <summary>
    /// Returns null if shouldn't respond.
    /// </summary>
    public string? GetReasonForResponding(FoulMessage message)
    {
        // Do not respond to yourself.
        if (message.SenderName == _config.BotName)
            return null;

        // User has replied to a bot.
        if (message.ReplyTo == _config.BotId)
            return "Reply";

        // Reply to all messages in private chat.
        if (_isPrivateChat)
            return "Private chat";

        // Reply to trigger words.
        var triggerKeyword = _config.KeyWords.FirstOrDefault(
            keyWord => message.Text.Contains(keyWord, StringComparison.InvariantCultureIgnoreCase));

        if (triggerKeyword != null)
            return $"Trigger word: '{triggerKeyword}'";

        // Do not reply if nothing from above.
        return null;
    }
}
