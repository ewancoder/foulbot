using System.Text.Json.Serialization;
using FoulBot.Domain.Storage;
using StackExchange.Redis;

namespace FoulBot.Infrastructure.Storage;

public class ChatParticipantConverter : JsonConverter<ChatParticipant?>
{
    public override ChatParticipant? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value == null ? null : new ChatParticipant(value);
    }

    public override void Write(
        Utf8JsonWriter writer, ChatParticipant? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.Name);
    }
}

public sealed class RedisContextStore : IContextStore
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters =
        {
            new ChatParticipantConverter()
        }
    };

    private readonly ILogger<RedisContextStore> _logger;
    private readonly IConnectionMultiplexer _redis;

    public RedisContextStore(
        ILogger<RedisContextStore> logger,
        IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async ValueTask<IEnumerable<FoulMessage>> GetLastAsync(FoulChatId chatId, int amount)
    {
        try
        {
            var db = _redis.GetDatabase();

            var context = await db.ListRangeAsync(GetKey(chatId), -amount, -1);

            // TODO: Properly implement nullable reference types handling here.
            // Including properly implementing converter for ChatParticipant.
            var deserialized = context
                .Select(value => JsonSerializer.Deserialize<FoulMessage>(value.ToString(), _jsonOptions)!)
                .Select(x => x with { Attachments = Enumerable.Empty<Attachment>() }) // Prevent null.
                .Where(x => x.Type == FoulMessageType.Text) // Do not serialize attachments.
                .Where(IsValid)
                .ToList();

            return deserialized;
        }
        catch (Exception exception)
        {
            // Do not fail all bots if persistence is unsuccessful.
            _logger.LogError(exception, "Could not load context to chat from Redis.");
            return Enumerable.Empty<FoulMessage>();
        }
    }

    public async ValueTask SaveMessageAsync(FoulChatId chatId, FoulMessage message)
    {
        if (message.Type != FoulMessageType.Text)
            return; // Do not serialize attachments.

        try
        {
            var db = _redis.GetDatabase();

            var serialized = JsonSerializer.Serialize(message, _jsonOptions);

            await db.ListRightPushAsync(GetKey(chatId), [serialized]);
        }
        catch (Exception exception)
        {
            // Do not fail all bots if persistence is unsuccessful.
            _logger.LogError(exception, "Could not save a message from context to Redis.");
        }
    }

    private bool IsValid(FoulMessage message)
    {
        // TODO: Use separate model for Redis storage, do not just blindly serialize.
        // We need this method to get rid of bad messages after we upgrade FoulBot schema.

        return message.Type > 0
            && message.Text is not null
            && message.SenderType > 0;
    }

    private static string GetKey(FoulChatId chatId) => chatId.IsPrivate
        ? $"chat_context_{chatId.Value}_{chatId.FoulBotId!.Value.BotId}"
        : $"chat_context_{chatId.Value}";
}
