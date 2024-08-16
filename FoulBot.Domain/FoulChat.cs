namespace FoulBot.Domain;

public sealed class FoulChat : IFoulChat
{
    private readonly Guid _chatInstanceId = Guid.NewGuid();
    private readonly ILogger<FoulChat> _logger;
    private readonly DateTime _chatCreatedAt;
    private readonly Dictionary<string, FoulMessage> _contextMessages = new Dictionary<string, FoulMessage>();
    private readonly List<FoulMessage> _context = new List<FoulMessage>(1000);
    private readonly object _lock = new object();
    private bool _isStopping;

    public FoulChat(ILogger<FoulChat> logger, FoulChatId chatId, bool isPrivateChat)
    {
        _logger = logger;
        ChatId = chatId;
        IsPrivateChat = isPrivateChat;
        _chatCreatedAt = DateTime.UtcNow;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Created instance of FoulChat with start time {StartTime}.", _chatCreatedAt);
    }

    private IScopedLogger Logger => _logger
        .AddScoped("ChatInstanceId", _chatInstanceId)
        .AddScoped("ChatId", ChatId);

    public bool IsPrivateChat { get; }
    public FoulChatId ChatId { get; }
    public event EventHandler<FoulMessage>? MessageReceived;
    public event EventHandler<FoulStatusChanged>? StatusChanged;

    public List<FoulMessage> GetContextSnapshot()
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
                _logger.LogWarning(exception, "Concurrency error when getting the context snapshot. Trying again.");
            }
        }
    }

    public void ChangeBotStatus(string whoName, string? byName, BotChatStatus status)
    {
        using var _ = Logger.BeginScope();

        if (_isStopping)
        {
            _logger.LogWarning("Gracefully stopping the application. Skipping status update {WhoName}, {ByName}, {Status}", whoName, byName, status);
            return;
        }

        _logger.LogDebug("Notifying bots about status change: {WhoName} was changed by {ByName}, {Status}", whoName, byName, status);

        StatusChanged?.Invoke(this, new FoulStatusChanged(whoName, byName, status));
    }

    public void HandleMessage(FoulMessage message)
    {
        // TODO: Consider including Message to the scope, but filter out all non-relevant fields.
        using var l = Logger
            .AddScoped("MessageId", message.Id)
            .BeginScope();

        if (_isStopping)
        {
            _logger.LogWarning("Gracefully stopping the application. Skipping message update.");
            return;
        }

        _logger.LogInformation("Received message by FoulChat");

        // Allow reading messages for the last minute, to not filter out the first message in the chat.
        if (message.Date < _chatCreatedAt.AddMinutes(-1))
        {
            _logger.LogTrace("Skipping out old message since it's date {MessageTime} is less than started date {StartTime}", message.Date, _chatCreatedAt);
            return;
        }

        if (!AddFoulMessageToContext(message))
        {
            _logger.LogDebug("Skipping this message by rules.");
            return;
        }

        // Mega super duper HACK to wait for the message from ALL the bots.
        _logger.LogTrace("Entering a very hacky 2-second way to consolidate the same message from different bots");

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);

            // For some reason logging scope is still present here (using Serilog).
            // Which is Great for me, but need to investigate why.
            // !!! It even bleeds through the event into the ensuing FoulBot instance which is wild.

            // TODO: Consider debouncing at this level.
            _logger.LogTrace("Finished waiting for 2 seconds in super-hack, now notifying bots about the message.");
            _logger.LogInformation("Notifying bots about the message: {@Message}, From {SenderName}, Text {MessageText}", message, message.SenderName, message.Text);

            try
            {
                MessageReceived?.Invoke(this, message);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Error during invoking MessageReceived event handler.");
            }
        });
    }

    public void AddMessage(FoulMessage message)
    {
        if (_isStopping)
        {
            _logger.LogWarning("Gracefully stopping the application. Skipping adding message to context.");
            return;
        }

        lock (_lock)
        {
            _logger.LogDebug("Entered a lock for adding the message to the context manually. Chat {ChatId}, message {@Message}.", ChatId, message);
            // TODO: Consider storing separate contexts for separate bots cause they might not be talking for a long time while others do.

            _context.Add(message);
            _contextMessages.Add(message.Id, message);

            CleanupContext();

            // TODO: Consider debouncing at this level (see handleupdate method to consolidate).
            _logger.LogDebug("Notifying bots about the manual message {@Message}.", message);
            MessageReceived?.Invoke(this, message);
        }
    }

    public Task GracefullyStopAsync()
    {
        _isStopping = true;
        return Task.Delay(TimeSpan.FromSeconds(5)); // HACK implementation.
    }

    private bool AddFoulMessageToContext(FoulMessage message)
    {
        if (_contextMessages.ContainsKey(message.Id) && message.ReplyTo == null)
        {
            _logger.LogDebug("Message has already been added to context by another bot and it doesn't need an update, skipping.");
            return false;
        }

        _logger.LogDebug("Entering lock for adding (updating) message to context.");
        lock (_lock)
        {
            _logger.LogDebug("Entered lock for adding (updating) message to context.");
            if (_contextMessages.ContainsKey(message.Id) && message.ReplyTo == null)
            {
                _logger.LogDebug("Message has already been added to context by another bot and it doesn't need an update, skipping.");
                return false;
            }

            if (_contextMessages.ContainsKey(message.Id))
            {
                _logger.LogDebug("Message has already been added to context by another bot, but this one has ReplyToMessage set. Updating the property and skipping the message.");
                var existing = _contextMessages[message.Id];
                existing.ReplyTo = message.ReplyTo;
                return false; // Discard the message after updating existing message.
            }

            {
                _context.Add(message);
                _contextMessages.Add(message.Id, message);
                _logger.LogDebug("Added message to context.");

                CleanupContext();

                _logger.LogDebug("Exiting the lock for adding message to context.");
                return true;
            }
        }
    }

    private void CleanupContext()
    {
        if (_context.Count > 400) // TODO: Make these numbers configurable.
        {
            _logger.LogDebug("Context has more than 400 messages. Cleaning it up to 300.");
            while (_context.Count > 300)
            {
                var msg = _context[0];
                _context.RemoveAt(0);
                _contextMessages.Remove(msg.Id);
            }
            _logger.LogDebug("Successfully cleaned up the context.");
        }
    }
}
