namespace FoulBot.Domain;

public readonly record struct ChatParticipant(string Name);

public sealed class ChatScopedBotMessenger(
    IBotMessenger messenger,
    FoulChatId chatId,
    CancellationToken cancellationToken) // TODO: Pass CancellationToken to messenger.
{
    public ValueTask<bool> CheckCanWriteAsync()
        => messenger.CheckCanWriteAsync(chatId);

    public ValueTask SendTextMessageAsync(string message)
        => messenger.SendTextMessageAsync(chatId, message);

    public ValueTask SendStickerAsync(string stickerId)
        => messenger.SendStickerAsync(chatId, stickerId);
}

/// <summary>
/// Handles logic of processing messages and deciding whether to reply.
/// </summary>
public sealed class FoulBotNg : IAsyncDisposable
{
    private readonly ChatScopedBotMessenger _botMessenger;
    private readonly IFoulChat _chat;
    private readonly CancellationTokenSource _cts;
    private readonly FoulBotConfiguration _config;

    private FoulBotNg(
        ChatScopedBotMessenger botMessenger,
        IFoulChat chat,
        CancellationTokenSource cts,
        FoulBotConfiguration config)
    {
        _botMessenger = botMessenger;
        _chat = chat;
        _cts = cts;
        _config = config;

        // Consider moving it outside (don't forget DisposeAsync).
        _chat.MessageReceived += OnMessageReceived;
    }

    public async ValueTask DisposeAsync()
    {
        _chat.MessageReceived -= OnMessageReceived;

        await _cts.CancelAsync();
        _cts.Dispose();
    }

    public static async ValueTask<FoulBotNg> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);

        var canWriteToChat = await messenger.CheckCanWriteAsync();
        if (!canWriteToChat)
            throw new InvalidOperationException("Bot cannot write to chat.");

        // Do not create disposable bot instance unless it can write to chat.
        return new FoulBotNg(messenger, chat, cts, config);

    }

    public static async ValueTask<FoulBotNg> InviteBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        FoulBotConfiguration config,
        ChatParticipant invitedBy)
    {
        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);

        var canWriteToChat = await messenger.CheckCanWriteAsync();
        if (!canWriteToChat)
            throw new InvalidOperationException("Bot cannot write to chat.");

        await GreetEveryoneAsync(messenger, aiClient, config, random, invitedBy);

        // Do not create disposable bot instance unless it can write to chat.
        return new FoulBotNg(messenger, chat, cts, config);
    }

    private void OnMessageReceived(object? sender, FoulMessage message)
        => _ = OnMessageReceivedAsync(message);

    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        // Test code.
        await _botMessenger.SendTextMessageAsync(message.Text);
    }

    private static async ValueTask GreetEveryoneAsync(
        ChatScopedBotMessenger messenger,
        IFoulAIClient aiClient,
        FoulBotConfiguration config,
        ISharedRandomGenerator random,
        ChatParticipant invitedBy)
    {
        if (config.Stickers.Count != 0)
        {
            var stickerIndex = random.Generate(0, config.Stickers.Count - 1);
            var stickerId = config.Stickers[stickerIndex];

            await messenger.SendStickerAsync(stickerId);
        }

        var directive = $"{config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy.Name}, tell them hello in your manner or thank the person for adding you if you feel like it.";
        var greetingsMessage = await aiClient.GetCustomResponseAsync(directive);
        await messenger.SendTextMessageAsync(greetingsMessage);
    }
}

public interface IFoulBotNgFactory
{
    ValueTask<FoulBotNg> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config);

    ValueTask<FoulBotNg> InviteBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config,
        ChatParticipant invitedBy);
}

public sealed class FoulBotNgFactory : IFoulBotNgFactory
{
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClientFactory _aiClientFactory;

    public FoulBotNgFactory(
        ISharedRandomGenerator random,
        IFoulAIClientFactory aiClientFactory)
    {
        _random = random;
        _aiClientFactory = aiClientFactory;
    }

    public ValueTask<FoulBotNg> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        return FoulBotNg.JoinBotToChatAsync(
            botMessenger,
            chat,
            config);
    }

    public ValueTask<FoulBotNg> InviteBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config,
        ChatParticipant invitedBy)
    {
        var aiClient = _aiClientFactory.Create(config.OpenAIModel);

        return FoulBotNg.InviteBotToChatAsync(
            botMessenger,
            chat,
            _random,
            aiClient,
            config,
            invitedBy);
    }
}
