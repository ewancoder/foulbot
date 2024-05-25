using System;
using System.Net;
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

    public TelegramUpdateHandler(
        ILogger<TelegramBotMessenger> botMessengerLogger, // TODO: Move to another factory.
        ILogger<TelegramUpdateHandler> logger,
        ChatPool chatPool,
        IFoulBotFactory botFactory,
        IFoulMessageFactory foulMessageFactory,
        FoulBotConfiguration botConfiguration)
    {
        _botMessengerLogger = botMessengerLogger;
        _logger = logger;
        _chatPool = chatPool;
        _botFactory = botFactory;
        _foulMessageFactory = foulMessageFactory;
        _botConfiguration = botConfiguration;

        using var _ = Logger.BeginScope();
        _logger.LogInformation("Initialized TelegramUpdateHandler with configuration {@Configuration}.", _botConfiguration);
    }

    private IScopedLogger Logger => _logger
        .AddScoped("BotId", _botConfiguration.BotId);

    private int _scheduledPollingError;

    public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        // This thing happens for couple second every two days with Bad Gateway error during midnight.
        // Handle this separately.
        using var _ = Logger.BeginScope();

        if (DateTime.UtcNow.Hour <= 2
            && exception is ApiRequestException arException
            && arException.HttpStatusCode == HttpStatusCode.BadGateway)
        {
            var now = Interlocked.Exchange(ref _scheduledPollingError, 1);
            if (now == 0) // This should happen only once per 10 seconds during the outburst of the error.
            {
                // This was the first exchange.
                // Intentionally not awaited.
                Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    _logger.LogWarning(exception, "Polling error occurred during potential Telegram downtime.");
                    now = 0;
                });
            }

            // Ignore duplicate exceptions.
            return Task.CompletedTask;
        }

        _logger.LogError(exception, "Polling error occurred.");

        return Task.CompletedTask;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message?.Text == "$collect_garbage")
        {
            GC.Collect();
            return;
        }

        using var _ = Logger.BeginScope();

        // Consider caching instances for every botClient instead of creating a lot of them.
        var botMessenger = new TelegramBotMessenger(_botMessengerLogger, botClient);

        if (DateTime.UtcNow < _coldStarted)
        {
            _logger.LogInformation("Handling update on cold start, delaying for 2 seconds.");
            await Task.Delay(2000);
        }

        _logger.LogDebug("Received update {@Update}.", update);

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
