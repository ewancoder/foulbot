using System.Globalization;
using PidorBot.AI;

namespace PidorBot.Brain;

/// <summary>
/// What the bot can do in the chat during its turn. Methods return a short result for the LLM.
/// </summary>
public interface IChatActions
{
    /// <param name="remember">False when the LLM broke the character: send, but don't remember it.</param>
    Task SayAsync(string text, bool remember, CancellationToken cancellationToken);
    Task<string> ReactAsync(int messageId, string emoji, CancellationToken cancellationToken);
    Task<string> SendStickerAsync(string emoji, CancellationToken cancellationToken);
    Task<string> SendImageAsync(string prompt, string? caption, CancellationToken cancellationToken);
    Task<string> SetReminderAsync(DateTime localTime, string what, bool daily, CancellationToken cancellationToken);
    Task<string> CancelReminderAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// Tools the LLM picks from when it acts in the chat. Text it writes is sent as a regular message.
/// </summary>
public static class BotTools
{
    public const string React = "react";
    public const string SendSticker = "send_sticker";
    public const string SendImage = "send_image";
    public const string SetReminder = "set_reminder";
    public const string CancelReminder = "cancel_reminder";

    private const string LocalTimeFormat = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// Emojis Telegram allows as reactions.
    /// </summary>
    public static readonly IReadOnlyList<string> ReactionEmojis =
    [
        "👍", "👎", "❤", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩", "🤮", "💩", "🙏", "👌", "🕊", "🤡",
        "🥱", "🥴", "😍", "🐳", "❤‍🔥", "🌚", "🌭", "💯", "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓", "🍾", "💋", "🖕", "😈",
        "😴", "😭", "🤓", "👻", "👨‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍", "🤗", "🫡", "🎅", "🎄", "☃", "💅", "🤪", "🗿",
        "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾", "🤷‍♂", "🤷", "🤷‍♀", "😡"
    ];

    public static LlmTool ReactTool { get; } = new(
        React,
        "Put an emoji reaction on a message, like people do in Telegram. Messages are prefixed with [msg N], N is the message_id.",
        $$"""
        {
          "type": "object",
          "properties": {
            "message_id": { "type": "integer" },
            "emoji": { "type": "string", "enum": {{JsonSerializer.Serialize(ReactionEmojis)}} }
          },
          "required": ["message_id", "emoji"]
        }
        """);

    public static LlmTool StickerTool(IReadOnlyList<string> emojis) => new(
        SendSticker,
        $"Send a sticker. You have stickers for these emojis: {string.Join(' ', emojis)}",
        """
        {
          "type": "object",
          "properties": { "emoji": { "type": "string", "description": "Emoji of the sticker." } },
          "required": ["emoji"]
        }
        """);

    public static LlmTool ImageTool { get; } = new(
        SendImage,
        "Draw a picture and send it to the chat: a meme, a caricature, whatever you feel like or were asked for. Takes a while, don't overuse it.",
        """
        {
          "type": "object",
          "properties": {
            "prompt": { "type": "string", "description": "Detailed description of the picture for the image model, in English." },
            "caption": { "type": "string", "description": "Optional caption under the picture, in your style." }
          },
          "required": ["prompt"]
        }
        """);

    public static LlmTool SetReminderTool { get; } = new(
        SetReminder,
        "Set a reminder when someone asks you to remind them (or someone else) about something. When the time comes, you will be told to remind them.",
        $$"""
        {
          "type": "object",
          "properties": {
            "time": { "type": "string", "description": "Local time when to remind, {{LocalTimeFormat}}." },
            "what": { "type": "string", "description": "What to remind about and whom, with all the details." },
            "daily": { "type": "boolean", "description": "Repeat every day at this time." }
          },
          "required": ["time", "what"]
        }
        """);

    public static LlmTool CancelReminderTool { get; } = new(
        CancelReminder,
        "Cancel a reminder from the list of reminders you've set.",
        """
        {
          "type": "object",
          "properties": { "id": { "type": "string" } },
          "required": ["id"]
        }
        """);

    public static async Task<string> ExecuteAsync(LlmToolCall call, IChatActions actions, CancellationToken cancellationToken)
    {
        try
        {
            using var arguments = JsonDocument.Parse(call.Arguments.Length == 0 ? "{}" : call.Arguments);
            var root = arguments.RootElement;

            return call.Name switch
            {
                React => await actions.ReactAsync(
                    root.GetProperty("message_id").GetInt32(), root.GetProperty("emoji").GetString()!, cancellationToken),
                SendSticker => await actions.SendStickerAsync(root.GetProperty("emoji").GetString()!, cancellationToken),
                SendImage => await actions.SendImageAsync(
                    root.GetProperty("prompt").GetString()!, OptionalString(root, "caption"), cancellationToken),
                SetReminder => await actions.SetReminderAsync(
                    DateTime.ParseExact(root.GetProperty("time").GetString()!, LocalTimeFormat, CultureInfo.InvariantCulture),
                    root.GetProperty("what").GetString()!,
                    root.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.True,
                    cancellationToken),
                CancelReminder => await actions.CancelReminderAsync(root.GetProperty("id").GetString()!, cancellationToken),
                _ => $"Error: unknown tool {call.Name}."
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return $"Error: invalid arguments ({exception.Message}).";
        }
    }

    public static string FormatLocalTime(DateTime localTime) => localTime.ToString(LocalTimeFormat, CultureInfo.InvariantCulture);

    private static string? OptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
