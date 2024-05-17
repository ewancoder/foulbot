using System;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types;

namespace FoulBot.Api;

public interface IFoulChat
{
    ChatId ChatId { get; }
    event EventHandler<FoulMessage> MessageReceived;

    void AddMessage(FoulMessage foulMessage);
    List<FoulMessage> GetContextSnapshot();
}

public sealed class FoulChat : IFoulChat
{
    private readonly DateTime _chatBotCreatedAt = DateTime.UtcNow;
    private readonly HashSet<string> _processedMessages = new HashSet<string>();
    private readonly List<FoulMessage> _context = new List<FoulMessage>(1000);
    private readonly object _lock = new object();

    public FoulChat(ChatId chatId)
    {
        ChatId = chatId;
    }

    public ChatId ChatId { get; }
    public event EventHandler<FoulMessage>? MessageReceived;

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
                Console.WriteLine(exception);
            }
        }
    }

    public void HandleUpdate(Update update)
    {
        if (update?.Message?.Date < _chatBotCreatedAt)
            return;

        var message = GetFoulMessageFromTelegramUpdate(update);
        if (message == null)
            return;

        // TODO: Consider debouncing at this level.
        MessageReceived?.Invoke(this, message);
    }

    private FoulMessage? GetFoulMessageFromTelegramUpdate(Update update)
    {
        if (update.Message == null || update.Message.Text == null || GetSenderName(update) == null)
            return null;

        var messageId = GetUniqueMessageId(update.Message);
        if (_processedMessages.Contains(messageId))
            return null;

        lock (_lock)
        {
            if (_processedMessages.Contains(messageId))
                return null;

            var message = new FoulMessage(
                messageId,
                FoulMessageType.User,
                GetSenderName(update),
                update.Message.Text,
                update.Message.ReplyToMessage?.From?.Username,
                update.Message.Date,
                false);
            _context.Add(message);
            _processedMessages.Add(messageId);

            // TODO: This is duplicate code.
            if (_context.Count > 900) // TODO: Make these numbers configurable.
            {
                while (_context.Count > 600)
                {
                    var msg = _context[0];
                    _context.RemoveAt(0);
                    _processedMessages.Remove(msg.Id);
                }
            }

            return message;
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
            // TODO: Consider storing separate contexts for separate bots cause they might not be talking for a long time while others do.

            _context.Add(foulMessage);

            if (_context.Count > 900) // TODO: Make these numbers configurable.
            {
                while (_context.Count > 600)
                {
                    var msg = _context[0];
                    _context.RemoveAt(0);
                    _processedMessages.Remove(msg.Id);
                }
            }

            // TODO: Consider debouncing at this level (see handleupdate method to consolidate).
            MessageReceived?.Invoke(this, foulMessage);
        }
    }
}
