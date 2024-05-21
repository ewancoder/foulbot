using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public interface IFoulChat
{
    ChatId ChatId { get; }
    event EventHandler<FoulMessage> MessageReceived;
    event EventHandler<FoulStatusChanged> StatusChanged;

    List<FoulMessage> GetContextSnapshot();
    void ChangeBotStatus(string whoName, string? byName, ChatMemberStatus status);
    void HandleTelegramMessage(Message telegramMessage);
    void AddMessage(FoulMessage foulMessage);
}

public sealed class FoulChat : IFoulChat
{
    private readonly ILogger<FoulChat> _logger;
    private readonly DateTime _chatCreatedAt;
    private readonly Dictionary<string, FoulMessage> _contextMessages = new Dictionary<string, FoulMessage>();
    private readonly List<FoulMessage> _context = new List<FoulMessage>(1000);
    private readonly object _lock = new object();

    public FoulChat(ILogger<FoulChat> logger, ChatId chatId)
    {
        _logger = logger;
        ChatId = chatId;
        _chatCreatedAt = DateTime.UtcNow;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Created instance of FoulChat with start time {StartTime}.", _chatCreatedAt);
    }

    private IScopedLogger Logger => _logger
        .AddScoped("ChatId", ChatId);

    public ChatId ChatId { get; }
    public event EventHandler<FoulMessage>? MessageReceived;
    public event EventHandler<FoulStatusChanged>? StatusChanged;

    public List<FoulMessage> GetContextSnapshot()
    {
        // Instead of locking.
        while (true)
        {
            try
            {
                var snapshot = _context.ToList();
                return snapshot;
            }
            catch (Exception exception)
            {
                using var _ = Logger.BeginScope();
                _logger.LogWarning(exception, "Concurrency error when getting the context snapshot. Trying again.");
            }
        }
    }

    public void ChangeBotStatus(string whoName, string? byName, ChatMemberStatus status)
    {
        using var _ = Logger.BeginScope();

        _logger.LogDebug("Notifying bots about status change: {WhoName} was changed by {ByName} into status {Status}", whoName, byName, status);

        StatusChanged?.Invoke(this, new FoulStatusChanged(whoName, byName, status));
    }

    public void HandleTelegramMessage(Message telegramMessage)
    {
        // TODO: Consider including Message to the scope, but filter out all non-relevant fields.
        using var l = Logger
            .AddScoped("MessageId", telegramMessage.MessageId)
            .BeginScope();

        _logger.LogInformation("Received message by FoulChat");

        if (telegramMessage.Date < _chatCreatedAt)
        {
            _logger.LogTrace("Skipping out old message since it's date {MessageTime} is less than started date {StartTime}", telegramMessage.Date, _chatCreatedAt);
            return;
        }

        var message = GetFoulMessage(telegramMessage);
        if (message == null)
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
            _logger.LogInformation("Notifying bots about the message: {Message}", message);
            MessageReceived?.Invoke(this, message);
        });
    }

    public void AddMessage(FoulMessage foulMessage)
    {
        lock (_lock)
        {
            _logger.LogDebug("Entered a lock for adding the message to the context manually. Chat {chatId}, message {@message}.", ChatId, foulMessage);
            // TODO: Consider storing separate contexts for separate bots cause they might not be talking for a long time while others do.

            _context.Add(foulMessage);
            _contextMessages.Add(foulMessage.Id, foulMessage);

            CleanupContext();

            // TODO: Consider debouncing at this level (see handleupdate method to consolidate).
            _logger.LogDebug("Notifying bots about the manual message {@message}.", foulMessage);
            MessageReceived?.Invoke(this, foulMessage);
        }
    }

    private FoulMessage? GetFoulMessage(Message telegramMessage)
    {
        var messageId = GetUniqueMessageId(telegramMessage);
        if (telegramMessage == null || telegramMessage.Text == null || GetSenderName(telegramMessage) == null)
        {
            _logger.LogWarning("Message text or sender name are null, skipping the message.");
            return null;
        }

        if (_contextMessages.ContainsKey(messageId) && telegramMessage.ReplyToMessage?.From?.Username == null)
        {
            _logger.LogDebug("Message has already been added to context by another bot and it doesn't need an update, skipping.");
            return null;
        }

        _logger.LogDebug("Entering lock for adding (updating) message to context.");
        lock (_lock)
        {
            _logger.LogDebug("Entered lock for adding (updating) message to context.");
            if (_contextMessages.ContainsKey(messageId) && telegramMessage.ReplyToMessage?.From?.Username == null)
            {
                _logger.LogDebug("Message has already been added to context by another bot and it doesn't need an update, skipping.");
                return null;
            }

            if (_contextMessages.ContainsKey(messageId))
            {
                _logger.LogDebug("Message has already been added to context by another bot, but this one has ReplyToMessage set. Updating the property and skipping the message.");
                var message = _contextMessages[messageId];
                message.ReplyTo = telegramMessage.ReplyToMessage.From.Username;
                return null; // Discard the message after updating existing message.
            }

            {
                var message = new FoulMessage(
                    messageId,
                    FoulMessageType.User,
                    GetSenderName(telegramMessage),
                    telegramMessage.Text,
                    telegramMessage.Date,
                    false)
                {
                    ReplyTo = telegramMessage.ReplyToMessage?.From?.Username
                };
                _context.Add(message);
                _contextMessages.Add(messageId, message);
                _logger.LogDebug("Added message to context.");

                CleanupContext();

                _logger.LogDebug("Exiting the lock for adding message to context.");
                return message;
            }
        }
    }

    private string GetUniqueMessageId(Message message)
    {
        return $"{message.From.Id}-{message.Date.Ticks}";
    }

    private string? GetSenderName(Message message)
    {
        // TODO: Remove all unsupported characters (normalize name).
        // Maybe do this on OpenAI side.
        if (message?.From == null)
            return null;

        if (message.From.FirstName == null && message.From.LastName == null)
            return null;

        if (message.From.FirstName == null)
            return message.From.LastName;

        if (message.From.LastName == null)
            return message.From.FirstName;

        return $"{message.From.FirstName}_{message.From.LastName}";
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
