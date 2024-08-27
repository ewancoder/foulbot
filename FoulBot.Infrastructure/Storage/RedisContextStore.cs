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

public sealed class RedisContextStore : IContextStore, IAsyncDisposable
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters =
        {
            new ChatParticipantConverter()
        }
    };

    private readonly ILogger<RedisContextStore> _logger;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private ConnectionMultiplexer? _redis;
    private bool _isDisposing;

    public RedisContextStore(
        ILogger<RedisContextStore> logger,
        string connectionString)
    {
        _logger = logger;
        _connectionString = connectionString;
    }

    public async ValueTask<IEnumerable<FoulMessage>> GetLastAsync(FoulChatId chatId, int amount)
    {
        try
        {
            var redis = await GetMultiplexerAsync();
            var db = redis.GetDatabase();

            var context = await db.ListRangeAsync(GetKey(chatId), -amount, -1);

            // TODO: Properly implement nullable reference types handling here.
            // Including properly implementing converter for ChatParticipant.
            var deserialized = context
                .Select(value => JsonSerializer.Deserialize<FoulMessage>(value.ToString(), _jsonOptions)!)
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
        try
        {
            var redis = await GetMultiplexerAsync();
            var db = redis.GetDatabase();

            var serialized = JsonSerializer.Serialize(message, _jsonOptions);

            await db.ListRightPushAsync(GetKey(chatId), [serialized]);
        }
        catch (Exception exception)
        {
            // Do not fail all bots if persistence is unsuccessful.
            _logger.LogError(exception, "Could not save a message from context to Redis.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isDisposing)
        {
            _isDisposing = true;
            await _cts.CancelAsync();
        }

        _cts.Dispose();
        if (_redis != null)
            await _redis.DisposeAsync();
        _lock.Dispose();
    }

    private async ValueTask<ConnectionMultiplexer> GetMultiplexerAsync()
    {
        if (_redis != null)
            return _redis;

        await _lock.WaitAsync();
        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(_connectionString);
            return _redis;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string GetKey(FoulChatId chatId) => chatId.IsPrivate
        ? $"chat_context_{chatId.Value}_{chatId.FoulBotId!.Value.BotId}"
        : $"chat_context_{chatId.Value}";
}
