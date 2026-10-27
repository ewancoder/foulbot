using PidorBot.Chats;

namespace PidorBot.Brain;

public enum ReplyReason
{
    None = 0,
    PrivateChat,
    ReplyToBot,
    Trigger,
    Keyword
}

/// <summary>
/// Decides whether the bot should reply to new messages. Same rules as in the old FoulBot.
/// </summary>
public sealed class ReplyPolicy
{
    /// <summary>
    /// Keywords make the bot interject only once in a while.
    /// </summary>
    public static readonly TimeSpan KeywordCooldown = TimeSpan.FromHours(1);

    // Whole words: the bot always replies.
    private static readonly string[] _triggers = ["пидор", "бот", "pidor", "bot"];

    // Anywhere in the text ("работа" contains "бот"): the bot replies once per KeywordCooldown.
    private static readonly string[] _keywords = ["пидор", "бот"];

    private static readonly char[] _wordPunctuation = [.. ".,!?;:()[]{}\"'«»…-—*_~"];

    private readonly string _mention;

    public ReplyPolicy(string botUsername)
    {
        _mention = $"@{botUsername}";
    }

    public ReplyReason Decide(
        IEnumerable<ChatMessage> newMessages, bool isPrivateChat, DateTime now, DateTime lastKeywordReplyAt)
    {
        var fromPeople = newMessages.Where(message => !message.IsFromBot).ToList();
        if (fromPeople.Count == 0)
            return ReplyReason.None;

        if (isPrivateChat)
            return ReplyReason.PrivateChat;

        if (fromPeople.Any(message => message.IsReplyToBot))
            return ReplyReason.ReplyToBot;

        if (fromPeople.Any(message => IsTrigger(message.Text)))
            return ReplyReason.Trigger;

        if (now - lastKeywordReplyAt >= KeywordCooldown
            && fromPeople.Any(message => HasKeyword(message.Text)))
            return ReplyReason.Keyword;

        return ReplyReason.None;
    }

    public bool IsTrigger(string text)
    {
        if (text.Contains(_mention, StringComparison.OrdinalIgnoreCase))
            return true;

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(_wordPunctuation))
            .Any(word => _triggers.Contains(word, StringComparer.InvariantCultureIgnoreCase));
    }

    private static bool HasKeyword(string text)
        => _keywords.Any(keyword => text.Contains(keyword, StringComparison.InvariantCultureIgnoreCase));
}
