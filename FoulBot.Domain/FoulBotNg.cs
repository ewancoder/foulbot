namespace FoulBot.Domain;

public record struct ChatParticipant(string Name);

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
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClient _aiClient;
    private readonly CancellationTokenSource _cts = new();
    private readonly FoulBotConfiguration _config;

    private FoulBotNg(
        IBotMessenger botMessenger,
        IFoulChat chat,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        FoulBotConfiguration config)
    {
        _botMessenger = new(botMessenger, chat.ChatId, _cts.Token);
        _chat = chat;
        _random = random;
        _aiClient = aiClient;
        _config = config;
    }

    public async ValueTask DisposeAsync()
    {
        _chat.MessageReceived -= OnMessageReceived;

        await _cts.CancelAsync();
        _cts.Dispose();
    }

    public async ValueTask<FoulBotNg> InviteBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        FoulBotConfiguration config,
        ChatParticipant invitedBy)
    {
        var canWriteToChat = await _botMessenger.CheckCanWriteAsync();
        if (!canWriteToChat)
            throw new InvalidOperationException("Bot cannot write to invited chat.");

        // Do not create disposable bot instance unless it can write to chat.
        var bot = new FoulBotNg(botMessenger, chat, random, aiClient, config);

        await GreetEveryoneAsync(invitedBy);

        // Consider moving is outside (don't forget DisposeAsync).
        _chat.MessageReceived += OnMessageReceived;

        return bot;
    }

    private void OnMessageReceived(object? sender, FoulMessage message)
        => _ = OnMessageReceivedAsync(message);

    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        // Test code.
        await _botMessenger.SendTextMessageAsync(message.Text);
    }

    private async ValueTask GreetEveryoneAsync(ChatParticipant invitedBy)
    {
        if (_config.Stickers.Count != 0)
        {
            var stickerIndex = _random.Generate(0, _config.Stickers.Count - 1);
            var stickerId = _config.Stickers[stickerIndex];

            await _botMessenger.SendStickerAsync(stickerId);
        }

        var directive = $"{_config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy.Name}, tell them hello in your manner or thank the person for adding you if you feel like it.";
        var greetingsMessage = await _aiClient.GetCustomResponseAsync(directive);
        await _botMessenger.SendTextMessageAsync(greetingsMessage);
    }
}
