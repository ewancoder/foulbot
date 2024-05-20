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
}

public sealed class FoulChat : IFoulChat
{
    private readonly DateTime _appStarted;
    private readonly HashSet<string> _processedMessages = new HashSet<string>();
    private readonly List<FoulMessage> _context = new List<FoulMessage>(1000);
    private readonly object _lock = new object();

    public FoulChat(ChatId chatId, DateTime appStarted)
    {
        ChatId = chatId;
        _appStarted = appStarted;
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
                Console.WriteLine(exception);
            }
        }
    }

    public void HandleUpdate(Update update)
    {
        if (update?.Message?.Date < _appStarted)
            return;

        var message = GetFoulMessageFromTelegramUpdate(update);
        if (message == null)
            return;

        // Mega super duper HACK to wait for the message from ALL the bots.
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);

            // TODO: Consider debouncing at this level.
            MessageReceived?.Invoke(this, message);
        });
    }

    private FoulMessage? GetFoulMessageFromTelegramUpdate(Update update)
    {
        if (update.Message == null || update.Message.Text == null || GetSenderName(update) == null)
            return null;

        var messageId = GetUniqueMessageId(update.Message);
        if (_processedMessages.Contains(messageId) && update.Message.ReplyToMessage?.From?.Username == null)
            return null;

        lock (_lock)
        {
            if (_processedMessages.Contains(messageId) && update.Message.ReplyToMessage?.From?.Username == null)
                return null;

            if (_processedMessages.Contains(messageId))
            {
                // TODO: Create dictionary.
                var message = _context.First(x => x.Id == messageId);
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

    public async ValueTask ChangeBotStatusAsync(string whoName, string? byName, ChatMemberStatus status)
    {
        StatusChanged?.Invoke(this, new FoulStatusChanged(whoName, byName, status));
    }
}
