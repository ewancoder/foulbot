using System.Text.Json.Serialization;
using PidorBot.Chats;
using StackExchange.Redis;

namespace PidorBot.Storage;

/// <summary>
/// Reads and writes the same Redis keys as the old FoulBot, so the bot keeps its chat history and hardcore mode.
/// New state (mood, memory) lives under pidorbot:* keys.
/// </summary>
public sealed class RedisChatStore : IChatStore
{
    // Only the last few hundred messages are ever loaded, keep some more just in case.
    public const int MaxStoredMessages = 1000;

    private readonly ILogger<RedisChatStore> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly BotIdentity _bot;

    public RedisChatStore(ILogger<RedisChatStore> logger, IConnectionMultiplexer redis, BotIdentity bot)
    {
        _logger = logger;
        _redis = redis;
        _bot = bot;
    }

    private IDatabase Db => _redis.GetDatabase();

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ChatRef chat, int count)
        => SafeAsync<IReadOnlyList<ChatMessage>>("load chat history", [], async () =>
        {
            var values = await Db.ListRangeAsync(HistoryKey(chat), -count, -1);

            return [.. values
                .Select(value => StoredMessage.Deserialize(value.ToString()))
                .OfType<StoredMessage>()
                .Select(message => message.ToChatMessage(_bot))];
        });

    public Task AddToHistoryAsync(ChatRef chat, ChatMessage message)
        => SafeAsync("save a message to chat history", async () =>
        {
            var key = HistoryKey(chat);
            await Db.ListRightPushAsync(key, StoredMessage.Serialize(message, _bot));
            await Db.ListTrimAsync(key, -MaxStoredMessages, -1);
        });

    public Task<string?> GetModeAsync(ChatRef chat)
        => SafeAsync("get bot mode", (string?)null, async () =>
        {
            var mode = await Db.StringGetAsync(Key(chat, "mode"));
            if (mode.HasValue)
                return mode.ToString();

            // The old $pidormode.
            return await Db.KeyExistsAsync(LegacySpecialModeKey(chat)) ? "hardcore" : null;
        });

    public Task SetModeAsync(ChatRef chat, string mode)
        => SafeAsync("set bot mode", async () =>
        {
            await Db.StringSetAsync(Key(chat, "mode"), mode);
            await Db.KeyDeleteAsync(LegacySpecialModeKey(chat));
        });

    public Task<MoodState?> GetMoodAsync(ChatRef chat)
        => GetJsonAsync<MoodState>(Key(chat, "mood"));

    public Task SetMoodAsync(ChatRef chat, MoodState mood)
        => SetJsonAsync(Key(chat, "mood"), mood);

    public Task<ChatMemory?> GetMemoryAsync(ChatRef chat)
        => GetJsonAsync<ChatMemory>(Key(chat, "memory"));

    public Task SetMemoryAsync(ChatRef chat, ChatMemory? memory)
        => SetJsonAsync(Key(chat, "memory"), memory);

    private Task<T?> GetJsonAsync<T>(string key) where T : class
        => SafeAsync($"get {key}", (T?)null, async () =>
        {
            var value = await Db.StringGetAsync(key);
            return value.HasValue ? JsonSerializer.Deserialize<T>(value.ToString()) : null;
        });

    private Task SetJsonAsync<T>(string key, T? value) where T : class
        => SafeAsync($"set {key}", async () =>
        {
            if (value == null)
                await Db.KeyDeleteAsync(key);
            else
                await Db.StringSetAsync(key, JsonSerializer.Serialize(value));
        });

    private async Task<T> SafeAsync<T>(string operation, T fallback, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not {Operation} in Redis", operation);
            return fallback;
        }
    }

    private Task SafeAsync(string operation, Func<Task> action)
        => SafeAsync(operation, true, async () =>
        {
            await action();
            return true;
        });

    private static string Key(ChatRef chat, string name) => $"pidorbot:{chat}:{name}";

    private string HistoryKey(ChatRef chat) => chat.IsPrivate
        ? $"chat_context_{chat}_{_bot.Username}"
        : $"chat_context_{chat}";

    private string LegacySpecialModeKey(ChatRef chat) => $"special_mode_{_bot.Username}_{chat}";
}

/// <summary>
/// Message in the format of the old FoulBot (FoulMessage), so the history is shared with it.
/// </summary>
internal sealed record StoredMessage(
    string? Id,
    int Type,
    int SenderType,
    string? Sender,
    string? Text,
    DateTime Date,
    bool IsOriginallyBotMessage,
    string? ReplyTo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SenderUsername,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TelegramId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PhotoFileId)
{
    private const int TextType = 1;
    private const int BotSenderType = 2;
    private const int UserSenderType = 3;

    public static string Serialize(ChatMessage message, BotIdentity bot) => JsonSerializer.Serialize(new StoredMessage(
        message.Id,
        TextType,
        message.IsFromBot ? BotSenderType : UserSenderType,
        message.SenderName,
        message.Text,
        message.Date,
        message.IsFromBot,
        message.IsReplyToBot ? bot.Username : null,
        message.SenderUsername,
        message.TelegramId,
        message.PhotoFileId));

    /// <summary>
    /// Returns null for messages that can't be used: documents, broken records.
    /// </summary>
    public static StoredMessage? Deserialize(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<StoredMessage>(json);

            return message is { Type: TextType, Text: not null, Sender.Length: > 0 } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public ChatMessage ToChatMessage(BotIdentity bot) => new(
        Id ?? Guid.NewGuid().ToString(),
        Sender!,
        SenderUsername,
        Text!,
        DateTime.SpecifyKind(Date, DateTimeKind.Utc),
        // Messages of other old FoulBot bots (Vova, Rick_Sanchez) are just people now.
        IsFromBot: SenderType == BotSenderType && Sender == BotIdentity.HistoryName,
        IsReplyToBot: ReplyTo == bot.Username)
    {
        TelegramId = TelegramId,
        PhotoFileId = PhotoFileId
    };
}
