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

    void AddMessage(FoulMessage foulMessage);
    List<FoulMessage> GetContextSnapshot();
    ValueTask ChangeBotStatusAsync(string whoName, string? byName, ChatMemberStatus status);
    void HandleUpdate(Update update);
}

public sealed class FoulChat : IFoulChat
{
    private readonly ILogger<FoulChat> _logger;
    private readonly DateTime _chatCreatedAt;
    private readonly HashSet<string> _processedMessages = new HashSet<string>();
    private readonly Dictionary<string, FoulMessage> _contextMessages = new Dictionary<string, FoulMessage>();
    private readonly List<FoulMessage> _context = new List<FoulMessage>(1000);
    private readonly object _lock = new object();

    public FoulChat(ILogger<FoulChat> logger, ChatId chatId)
    {
        _logger = logger;
        ChatId = chatId;
        _chatCreatedAt = DateTime.UtcNow;

        logger.LogInformation("Created instance of FoulChat {chatId} with start time {StartTime}.", chatId.Identifier, _chatCreatedAt);
    }

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
                _logger.LogWarning(exception, "Concurrency error when getting the context snapshot.");
            }
        }
    }

    public void HandleUpdate(Update update)
    {
        var messageId = GetUniqueMessageId(update.Message);

        _logger.LogInformation("Handling a message by FoulChat {chatId}, message {messageId}, update {@update}", ChatId.Identifier, messageId, update);
        if (update?.Message?.Date < _chatCreatedAt)
        {
            _logger.LogDebug("Filtering out old message since it's date {date} is less than started date {appStarted}, message {messageId}", update?.Message?.Date, _chatCreatedAt, messageId);
            return;
        }

        var message = GetFoulMessageFromTelegramUpdate(update);
        if (message == null)
        {
            _logger.LogDebug("Skipping notifying bots: rules say we should skip it (or it's a duplicate from another bot). Chat {chatId}, message {messageId}", ChatId, messageId);
            return;
        }

        // Mega super duper HACK to wait for the message from ALL the bots.
        _logger.LogDebug("Entering a very hacky 2-second way to consolidate the same message from different bots, chat {chatId}, message {messageId}", ChatId, messageId);
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);

            // TODO: Consider debouncing at this level.
            _logger.LogDebug("Finished waiting for 2 seconds in super-hack, notifying bots about the message. Chat {chatId}, message {messageId}", ChatId, messageId);
            MessageReceived?.Invoke(this, message);
        });
    }

    private FoulMessage? GetFoulMessageFromTelegramUpdate(Update update)
    {
        var messageId = GetUniqueMessageId(update.Message);
        if (update.Message == null || update.Message.Text == null || GetSenderName(update) == null)
        {
            _logger.LogDebug("Message text or sender name are null, skipping the message {messageId}", messageId);
            return null;
        }

        if (_processedMessages.Contains(messageId) && update.Message.ReplyToMessage?.From?.Username == null)
        {
            _logger.LogDebug("Message has already been processed and it's not a reply, skipping. Message {messageId}", messageId);
            return null;
        }

        lock (_lock)
        {
            _logger.LogDebug("Entered lock for adding the message to the CONTEXT. Message {messageId}", messageId);
            if (_processedMessages.Contains(messageId) && update.Message.ReplyToMessage?.From?.Username == null)
            {
                _logger.LogDebug("Message has already been processed and it's not a reply, skipping. Message {messageId}", messageId);
                return null;
            }

            if (_processedMessages.Contains(messageId))
            {
                _logger.LogDebug("Message has already been added by another bot, but this one has ReplyToMessage set. Updating the property and skipping the message {messageId}.", messageId);
                var message = _contextMessages[messageId];
                message.ReplyTo = update.Message.ReplyToMessage?.From?.Username;
                return null; // Discard the message after updating existing message.
            }

            {
                var message = new FoulMessage(
                    messageId,
                    FoulMessageType.User,
                    GetSenderName(update),
                    update.Message.Text,
                    update.Message.Date,
                    false)
                {
                    ReplyTo = update.Message.ReplyToMessage?.From?.Username
                };
                _context.Add(message);
                _processedMessages.Add(messageId);
                _contextMessages.Add(messageId, message);
                _logger.LogDebug("Added message to context and to processed messages, message {messageId} - {message}.", messageId, message);

                CleanupContext();

                _logger.LogDebug("Exiting the lock (after returning) for adding the message to the context, message {messageId}.", messageId);
                return message;
            }
        }
    }

    private string GetUniqueMessageId(Message message)
    {
        return $"{message.From.Id}-{message.Date.Ticks}";
    }

    private string? GetSenderName(Update update)
    {
        // TODO: Remove all unsupported characters (normalize name).
        // Maybe do this on OpenAI side.
        if (update.Message?.From == null)
            return null;

        if (update.Message.From.FirstName == null && update.Message.From.LastName == null)
            return null;

        if (update.Message.From.FirstName == null)
            return update.Message.From.LastName;

        if (update.Message.From.LastName == null)
            return update.Message.From.FirstName;

        return $"{update.Message.From.FirstName}_{update.Message.From.LastName}";
    }

    public void AddMessage(FoulMessage foulMessage)
    {
        lock (_lock)
        {
            _logger.LogDebug("Entered a lock for adding the message to the context manually. Chat {chatId}, message {@message}.", ChatId, foulMessage);
            // TODO: Consider storing separate contexts for separate bots cause they might not be talking for a long time while others do.

            _context.Add(foulMessage);
            _processedMessages.Add(foulMessage.Id);
            _contextMessages.Add(foulMessage.Id, foulMessage);

            CleanupContext();

            // TODO: Consider debouncing at this level (see handleupdate method to consolidate).
            _logger.LogDebug("Notifying bots about the manual message {@message}.", foulMessage);
            MessageReceived?.Invoke(this, foulMessage);
        }
    }

    public ValueTask ChangeBotStatusAsync(string whoName, string? byName, ChatMemberStatus status)
    {
        _logger.LogDebug("Notifying bots about status change: {who}, {by}, {status}, {chatId}", whoName, byName, status, ChatId);
        StatusChanged?.Invoke(this, new FoulStatusChanged(whoName, byName, status));
        return default;
    }

    private void CleanupContext()
    {
        if (_context.Count > 900) // TODO: Make these numbers configurable.
        {
            _logger.LogDebug("Context has more than 900 messages. Cleaning it up to 600.");
            while (_context.Count > 600)
            {
                var msg = _context[0];
                _context.RemoveAt(0);
                _processedMessages.Remove(msg.Id);
                _contextMessages.Remove(msg.Id);
            }
        }
    }
}
