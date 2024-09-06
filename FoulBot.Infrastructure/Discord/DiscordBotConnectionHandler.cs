using Discord;
using Discord.WebSocket;
using FoulBot.Domain.Connections;
using FoulBot.Domain.Storage;

namespace FoulBot.Infrastructure.Discord;

public sealed class DiscordBotConnectionHandler : IBotConnectionHandler
{
    private readonly ILogger<DiscordBotConnectionHandler> _logger;
    private readonly IDiscordBotMessengerFactory _botMessengerFactory;
    private readonly IFoulBotFactory _botFactory;
    private readonly IAllowedChatsProvider _allowedChats;
    private readonly ChatPool _chatPool;

    public DiscordBotConnectionHandler(
        ILogger<DiscordBotConnectionHandler> logger,
        IDiscordBotMessengerFactory botMessengerFactory,
        IFoulBotFactory botFactory,
        IAllowedChatsProvider allowedChats,
        ChatPool chatPool)
    {
        _logger = logger;
        _botMessengerFactory = botMessengerFactory;
        _botFactory = botFactory;
        _allowedChats = allowedChats;
        _chatPool = chatPool;
    }

    private IScopedLogger Logger => _logger
        .AddScoped();

    public async ValueTask<IBotMessenger> StartHandlingAsync(BotConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All
        });
        client.Log += LogAsync;

        var botMessenger = _botMessengerFactory.Create(client);
        client.MessageReceived += DiscordMessageReceived;

        await client.LoginAsync(TokenType.Bot, configuration.ConnectionString);
        await client.StartAsync();

        async Task DiscordMessageReceived(SocketMessage message)
        {
            using var _ = Logger.BeginScope();

            // TODO: Log more data.
            _logger.LogDebug("Received message {Message}.", message.Id);

            try
            {
                // TODO: pass cancellation token.
                await HandleUpdateAsync(new(configuration.Configuration.BotId, configuration.Configuration.BotName), message, chat => _botFactory.JoinBotToChatAsync(botMessenger!, chat, configuration.Configuration), cancellationToken: default);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to handle update {Message}.", message.Id);
            }
        }

        static async Task LogAsync(LogMessage message)
        {
            // TODO: Log.
        }

        return botMessenger;
    }

    private async ValueTask HandleUpdateAsync(
        FoulBotId foulBotId,
        SocketMessage message,
        JoinBotToChatAsync botFactory,
        CancellationToken cancellationToken)
    {
        if (message.Channel is not IGuildChannel guildChannel)
        {
            // TODO: Log this. In future - send message to private chats.
            // For now we only support group chats.
            return;
        }

        var chatId = new FoulChatId($"{guildChannel.Guild.Id}__{guildChannel.Id}");
        var messageText = message.Content;
        var authorName = message.Author.GlobalName;
        if (authorName == null)
            return; // DO NOT add this/other bots messages to context here. We are doing that automatically.
        // TODO: However think about relaxing this so OTHER bots can chat with mine. OR rework the whole app to allow bots see other bots etc.

        string? replyTo = null;
        if (message.Type == MessageType.Reply
            && message.MentionedUsers.Any(x => x.Id.ToString() == foulBotId.BotId))
            replyTo = foulBotId.BotId;

        if (messageText == "$activate")
        {
            await _allowedChats.AllowChatAsync(chatId);
            return;
        }
        if (messageText == "$deactivate")
        {
            await _allowedChats.DisallowChatAsync(chatId);
            return;
        }

        var foulMessage = FoulMessage.CreateText(
            message.Id.ToString(),
            FoulMessageSenderType.User,
            new(authorName),
            messageText,
            message.Timestamp.UtcDateTime,
            false,
            replyTo);

        await _chatPool.HandleMessageAsync(
            chatId,
            foulBotId,
            foulMessage,
            botFactory,
            cancellationToken);
    }
}
