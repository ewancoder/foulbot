using System.Globalization;

namespace PidorBot.Chats;

/// <summary>
/// Telegram chat. Private chats (with a single person) have positive ids, groups have negative ones.
/// </summary>
public readonly record struct ChatRef(long Id)
{
    public bool IsPrivate => Id > 0;

    public override string ToString() => Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The bot itself, as Telegram knows it.
/// </summary>
public sealed record BotIdentity(long Id, string Username)
{
    /// <summary>
    /// Name of the bot in the chat history. Kept from the old FoulBot so the existing history stays the bot's own.
    /// </summary>
    public const string HistoryName = "Pidor_Bot";
}

/// <param name="SenderName">Name that the LLM sees, like Ivan_Petrov.</param>
/// <param name="SenderUsername">Telegram username, used for custom names.</param>
public sealed record ChatMessage(
    string Id,
    string SenderName,
    string? SenderUsername,
    string Text,
    DateTime Date,
    bool IsFromBot,
    bool IsReplyToBot)
{
    /// <summary>
    /// Id of the message in Telegram, so the bot can react to it. Unknown for old messages.
    /// </summary>
    public int? TelegramId { get; init; }

    /// <summary>
    /// Telegram file id of the attached photo, so the bot can look at it.
    /// </summary>
    public string? PhotoFileId { get; init; }

    public static ChatMessage FromBot(string text, DateTime date) => new(
        Guid.NewGuid().ToString(), BotIdentity.HistoryName, null, text, date, IsFromBot: true, IsReplyToBot: false);

    public override string ToString() => $"({Id}) {SenderName}: {Text}";
}
