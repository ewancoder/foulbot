using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoulBot.Domain;
using FoulBot.Infrastructure;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed class TelegramUpdateHandler : IUpdateHandler
{
    private readonly ILogger<TelegramBotMessenger> _botMessengerLogger;
    private readonly ILogger<TelegramUpdateHandler> _logger;
    private readonly ChatPool _chatPool;
    private readonly IFoulBotFactory _botFactory;
    private readonly IFoulMessageFactory _foulMessageFactory;
    private readonly FoulBotConfiguration _botConfiguration;
    private readonly DateTime _coldStarted = DateTime.UtcNow + TimeSpan.FromSeconds(2); // Make a delay on first startup so all the bots are properly initialized.
    private readonly IEnumerable<string> _allowedChats;

    public TelegramUpdateHandler(
        ILogger<TelegramBotMessenger> botMessengerLogger, // TODO: Move to another factory.
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        IFoulMessageFactory foulMessageFactory,
        FoulBotConfiguration botConfiguration,
        IEnumerable<string> allowedChats)
    {
        _botMessengerLogger = botMessengerLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _foulMessageFactory = foulMessageFactory;
        _botConfiguration = botConfiguration;
        _allowedChats = allowedChats;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Initialized TelegramUpdateHandler with configuration {@Configuration}.", _botConfiguration);
    }

    private IScopedLogger Logger => _logger
        .AddScoped("BotId", _botConfiguration.BotId);

    private readonly HashSet<int> _pollingErrorCodes = new HashSet<int>();
    private readonly object _pollingErrorLock = new object();
    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // This thing happens for couple second every two days with Bad Gateway error during midnight.
        // Handle this separately.
        using var _ = Logger.BeginScope();

        if (exception is ApiRequestException arException)
        {
            if (_pollingErrorCodes.Contains(arException.ErrorCode))
                return Task.CompletedTask; // Ignore duplicate exceptions;

            lock (_pollingErrorLock)
            {
                if (_pollingErrorCodes.Contains(arException.ErrorCode))
                    return Task.CompletedTask; // Ignore duplicate exceptions;

                _pollingErrorCodes.Add(arException.ErrorCode);

                // Intentionally not awaited.
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    _logger.LogWarning(exception, "Polling error occurred during potential Telegram downtime.");
                    _pollingErrorCodes.Remove(arException.ErrorCode);
                });
            }
        }

        _logger.LogError(exception, "Polling error occurred.");

        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        using var _ = Logger.BeginScope();
        _logger.LogDebug("Received update {@Update}.", update);

        if (update.Message?.Text == "$collect_garbage")
        {
            _logger.LogDebug("Collect garbage commant has been issued. Collecting garbage.");
            GC.Collect();
            return;
        }

        // Consider caching instances for every botClient instead of creating a lot of them.
        var botMessenger = new TelegramBotMessenger(_botMessengerLogger, botClient);

        if (DateTime.UtcNow < _coldStarted)
        {
            _logger.LogInformation("Handling update on cold start, delaying for 2 seconds.");
            await Task.Delay(2000);
        }

        try
        {
            await HandleUpdateAsync(_botConfiguration.BotId, update, chat => _botFactory.Create(botMessenger, _botConfiguration, chat));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to handle update {@Update}.", update);
        }
    }

    private async ValueTask HandleUpdateAsync(string botId, Update update, Func<IFoulChat, IFoulBot> botFactory)
    {
        using var _ = Logger
            .AddScoped("BotId", botId)
            .AddScoped("ChatId", update?.MyChatMember?.Chat?.Id ?? update?.Message?.Chat?.Id)
            .AddScoped("ChatName", update?.MyChatMember?.Chat?.Username ?? update?.Message?.Chat?.Username
                ?? update?.MyChatMember?.Chat?.Title ?? update?.Message?.Chat?.Title)
            .BeginScope();

        if (update == null)
        {
            _logger.LogError("Received null update from Telegram.");
            return;
        }

        if (update.Type == UpdateType.MyChatMember)
        {
            _logger.LogDebug("Received MyChatMember update, initiating bot change status.");

            if (update.MyChatMember?.NewChatMember?.User?.Username == null
                || update.MyChatMember.Chat?.Id == null)
            {
                _logger.LogWarning("MyChatMember update doesn't have required properties. Skipping handling.");
                return;
            }

            var member = update.MyChatMember.NewChatMember;
            var chatId = update.MyChatMember.Chat.Id.ToString();
            var invitedByUsername = update.MyChatMember.From?.Username; // Who invited / kicked the bot.

            await _chatPool.UpdateStatusAsync(
                chatId,
                botId,
                member.User.Username,
                ToBotChatStatus(member.Status),
                invitedByUsername,
                update.MyChatMember.Chat.Type == ChatType.Private,
                botFactory);

            _logger.LogInformation("Successfully handled NewChatMember update.");
            return;
        }

        if (update.Type == UpdateType.Message && update.Message?.Type == MessageType.Text)
        {
            _logger.LogDebug("Received Message update, handling the message.");

            if (update.Message?.Chat?.Id == null)
            {
                _logger.LogWarning("Message update doesn't have required properties. Skipping handling.");
                return;
            }

            var chatId = update.Message.Chat.Id.ToString();
            if (!_allowedChats.Contains(chatId))
            {
                _logger.LogWarning("Received a message from not allowed chat.");
                return; // Bot is not allowed to write to this chat.
                        // This is a temporary measure so that bots don't reply to random people.
                        // In future this will be improved so that they still write 5-10 messages per day, unless allowed.
            }

            var message = _foulMessageFactory.CreateFrom(update.Message);
            if (message == null)
            {
                _logger.LogDebug("FoulMessage factory returned null, skipping sending message to the chat.");
                return;
            }

            await _chatPool.HandleMessageAsync(
                chatId,
                botId,
                message,
                update.Message.Chat.Type == ChatType.Private,
                botFactory);

            _logger.LogInformation("Successfully handled Message update.");
            return;
        }

        // TODO: Configure to only receive necessary types of updates.
        _logger.LogDebug("Received unnecessary update, skipping handling.");
    }

    private BotChatStatus ToBotChatStatus(ChatMemberStatus status)
    {
        if (status == ChatMemberStatus.Left || status == ChatMemberStatus.Kicked)
            return BotChatStatus.Left;

        return BotChatStatus.Joined;
    }
}
