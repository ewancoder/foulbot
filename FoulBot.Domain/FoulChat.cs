namespace FoulBot.Domain;

public interface IFoulChat
{
    event EventHandler<FoulMessage> MessageReceived;

    FoulChatId ChatId { get; }
    bool IsPrivateChat { get; }

    IList<FoulMessage> GetContextSnapshot();
    ValueTask HandleMessageAsync(FoulMessage message);
    void AddMessage(FoulMessage message);

    Task GracefullyCloseAsync();
}

public sealed class FoulChat : IFoulChat
{
    private readonly IDuplicateMessageHandler _duplicateMessageHandler;
    private readonly ILogger<FoulChat> _logger;
    private readonly Guid _chatInstanceId = Guid.NewGuid();
    private readonly DateTime _chatCreatedAt = DateTime.UtcNow;
    private readonly List<FoulMessage> _context = new(1000);
    private readonly Dictionary<string, FoulMessage> _contextMessages = [];
    private readonly object _lock = new();
    private bool _isStopping;

    public FoulChat(
        IDuplicateMessageHandler duplicateMessageHandler,
        ILogger<FoulChat> logger,
        FoulChatId chatId, bool isPrivateChat)
    {
        _duplicateMessageHandler = duplicateMessageHandler;
        _logger = logger;
        ChatId = chatId;
        IsPrivateChat = isPrivateChat;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Created instance of FoulChat with start time {StartTime}", _chatCreatedAt);
    }

    public event EventHandler<FoulMessage>? MessageReceived;

    private IScopedLogger Logger => _logger
        .AddScoped("ChatInstanceId", _chatInstanceId)
        .AddScoped("ChatId", ChatId); // TODO: Add name of the chat.

    public FoulChatId ChatId { get; }
    public bool IsPrivateChat { get; }

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

        if (!TryAddMessageToContext(message, out var _))
        {
            _logger.LogTrace("Skipping this message handling (as it's likely duplicate)");
            return;
        }

        // Mega super duper HACK to wait for the message from ALL the bots.
        _logger.LogTrace("Entering a very hacky 2-second way to consolidate the same message from different bots");

        await Task.Delay(2000);

        // For some reason logging scope is still present here (using Serilog).
        // Which is Great for me, but need to investigate why.
        // !!! It even bleeds through the event into the ensuing FoulBot instance which is wild.

        _logger.LogTrace("Finished waiting for 2 seconds in super-hack, now notifying bots about the message");

        TryAddMessageToContext(message, out var consolidatedMessage);

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

            _context.Add(message);
            _contextMessages.Add(message.Id, message);

            CleanupContext();

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

    private bool TryAddMessageToContext(FoulMessage message, out FoulMessage consolidatedMessage)
    {
        if (_contextMessages.TryGetValue(message.Id, out var duplicate))
        {
            var newMessage = _duplicateMessageHandler.Merge(duplicate, message);
            if (newMessage == null)
            {
                consolidatedMessage = duplicate;
                return false; // Message already exists but we don't need to update it.
            }
        }

        _logger.LogDebug("Entering lock for adding message to context.");
        lock (_lock)
        {
            if (_contextMessages.TryGetValue(message.Id, out duplicate))
            {
                var newMessage = _duplicateMessageHandler.Merge(duplicate, message);
                if (newMessage == null)
                {
                    consolidatedMessage = duplicate;
                    _logger.LogDebug("Exiting the lock for adding message to context.");
                    return false; // Message already exists but we don't need to update it.
                }

                _context.Remove(duplicate);
                _contextMessages.Remove(duplicate.Id);
            }

            {
                _context.Add(message);
                _contextMessages.Add(message.Id, message);
                _logger.LogDebug("Added message to context.");

                CleanupContext();

                consolidatedMessage = message;
                _logger.LogDebug("Exiting the lock for adding message to context.");
                return true;
            }
        }
    }

    private void CleanupContext()
    {
        if (_context.Count > 500) // TODO: Make these numbers configurable.
        {
            _logger.LogDebug("Context has more than 500 messages. Cleaning it up to 200.");
            while (_context.Count > 200)
            {
                var msg = _context[0];
                _context.RemoveAt(0);
                _contextMessages.Remove(msg.Id);
            }
            _logger.LogDebug("Successfully cleaned up the context.");
        }
    }
}
