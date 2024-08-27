using System.Collections.Concurrent;
using FoulBot.Domain.Storage;

namespace FoulBot.Domain;

public interface IFoulChat
{
    event EventHandler<FoulMessage>? MessageReceived;

    FoulChatId ChatId { get; }
    bool IsPrivateChat { get; }

    IList<FoulMessage> GetContextSnapshot();
    ValueTask HandleMessageAsync(FoulMessage message);
    void AddMessage(FoulMessage message);

    Task GracefullyCloseAsync();
}

public sealed class FoulChat : IFoulChat
{
    // Consider making these numbers configurable.
    public const int MaxContextSizeLimit = 500;
    public const int MinContextSize = 200;

    private readonly TimeProvider _timeProvider;
    private readonly IDuplicateMessageHandler _duplicateMessageHandler;
    private readonly IContextStore _contextStore;
    private readonly ILogger<FoulChat> _logger;
    private readonly Guid _chatInstanceId = Guid.NewGuid();
    private readonly DateTime _chatCreatedAt = DateTime.UtcNow;
    private readonly IList<FoulMessage> _context;
    private readonly ConcurrentDictionary<string, List<FoulMessage>> _unconsolidatedMessages = [];
    private readonly object _lock = new();
    private bool _isStopping;

    // TODO: Unit test non-empty context on startup (when using factory).
    private FoulChat(
        TimeProvider timeProvider,
        IDuplicateMessageHandler duplicateMessageHandler,
        IContextStore contextStore,
        ILogger<FoulChat> logger,
        FoulChatId chatId,
        IList<FoulMessage> context)
    {
        _timeProvider = timeProvider;
        _duplicateMessageHandler = duplicateMessageHandler;
        _contextStore = contextStore;
        _logger = logger;
        ChatId = chatId;
        _context = context;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Created instance of FoulChat with start time {StartTime}", _chatCreatedAt);
    }

    // TODO: Unit test this.
    public static async ValueTask<FoulChat> CreateFoulChatAsync(
        TimeProvider timeProvider,
        IDuplicateMessageHandler duplicateMessageHandler,
        IContextStore contextStore,
        ILogger<FoulChat> logger,
        FoulChatId chatId)
    {
        var context = await contextStore.GetLastAsync(chatId, MinContextSize);

        return new FoulChat(
            timeProvider,
            duplicateMessageHandler,
            contextStore,
            logger,
            chatId,
            context.ToList());
    }

    public event EventHandler<FoulMessage>? MessageReceived;

    private IScopedLogger Logger => _logger
        .AddScoped("ChatInstanceId", _chatInstanceId)
        .AddScoped("ChatId", ChatId); // TODO: Add name of the chat.

    public FoulChatId ChatId { get; }
    public bool IsPrivateChat => ChatId.IsPrivate;

    public IList<FoulMessage> GetContextSnapshot()
    {
        // Instead of locking.
        while (true)
        {
            try
            {
                var snapshot = _context
                    .OrderBy(x => x.Date) // Order by date so we're sure context is in correct order at decision making step.
                    .ToList();

                return snapshot;
            }
            catch (Exception exception)
            {
                using var _ = Logger.BeginScope();
                _logger.LogWarning(exception, "Concurrency error when getting the context snapshot. Trying again");
            }
        }
    }

    public async ValueTask HandleMessageAsync(FoulMessage message)
    {
        using var l = Logger
            .AddScoped("MessageId", message.Id)
            .BeginScope();

        _logger.LogInformation("Received message by FoulChat: {@Message}", message);

        if (_isStopping)
        {
            _logger.LogWarning("Gracefully stopping the application. Skipping handling message");
            return;
        }

        // Allow reading messages for the last minute, to not filter out the first message in the chat.
        if (message.Date < _chatCreatedAt.AddMinutes(-1))
        {
            _logger.LogDebug("Skipping old message since it's date {MessageDate} is less than started date {StartTime}", message.Date, _chatCreatedAt);
            return;
        }

        var consolidatedMessage = await ConsolidateAndAddMessageToContextAsync(message);
        if (consolidatedMessage == null) // Other bots are too late.
            return;

        // TODO: Consider debouncing at this level.
        _logger.LogInformation("Notifying bots about the message");
        MessageReceived?.Invoke(this, consolidatedMessage);
    }

    public void AddMessage(FoulMessage message)
    {
        using var l = Logger
            .AddScoped("MessageId", message.Id)
            .BeginScope();

        _logger.LogInformation("Manually adding message to chat: {@Message}", message);

        if (_isStopping)
        {
            _logger.LogWarning("Gracefully stopping the application. Skipping adding message");
            return;
        }

        lock (_lock)
        {
            _logger.LogDebug("Entered a lock for adding the message to the context manually");

            AddMessageToContext(message);

            // TODO: Consider debouncing at this level.
            _logger.LogDebug("Notifying bots about the manual message");
            MessageReceived?.Invoke(this, message);
        }
    }

    public Task GracefullyCloseAsync()
    {
        if (_isStopping) return Task.CompletedTask;
        _isStopping = true;

        return Task.Delay(TimeSpan.FromSeconds(5)); // HACK implementation.
    }

    private async ValueTask<FoulMessage?> ConsolidateAndAddMessageToContextAsync(FoulMessage message)
    {
        var list = _unconsolidatedMessages.GetOrAdd(message.Id, []);

        lock (_lock)
        {
            list.Add(message);

            if (list.Count > 1)
                return null; // Only process the rest by the first handler.
        }

        // HACK: Waiting for messages from other bots to come.
        // This can be improved in future if Chat knew how many bots are in it.
        // TODO: Consider passing TimeProvider here to improve test execution time.
        await Task.Delay(TimeSpan.FromSeconds(2), _timeProvider);

        var consolidatedMessage = _duplicateMessageHandler.Merge(list);

        _logger.LogDebug("Entering lock for adding message to context.");
        lock (_lock)
        {
            AddMessageToContext(consolidatedMessage);
            _logger.LogDebug("Added message to context.");
            _logger.LogDebug("Exiting the lock for adding message to context.");
        }

        _unconsolidatedMessages.Remove(message.Id, out _);

        return consolidatedMessage;
    }

    private void CleanupContextIfNeeded()
    {
        if (_context.Count > MaxContextSizeLimit)
        {
            _logger.LogDebug("Context has more than {MaxContextMessages} messages. Cleaning it up to {MinContextMessages}", MaxContextSizeLimit, MinContextSize);
            var orderedByDate = _context.OrderBy(x => x.Date);
            foreach (var message in orderedByDate)
            {
                RemoveMessageFromContext(message);

                if (_context.Count <= MinContextSize)
                    break;
            }

            _logger.LogDebug("Successfully cleaned up the context.");
        }
    }

    /// <summary>
    /// Should only be called within a lock.
    /// </summary>
    private void AddMessageToContext(FoulMessage message)
    {
        _context.Add(message);

        // TODO: Await this. For now hacky implementation to just save and forget.
        _ = _contextStore.SaveMessageAsync(ChatId, message).AsTask();

        CleanupContextIfNeeded();
    }

    /// <summary>
    /// Should only be called within a lock.
    /// </summary>
    private void RemoveMessageFromContext(FoulMessage message)
    {
        _context.Remove(message);
    }
}
