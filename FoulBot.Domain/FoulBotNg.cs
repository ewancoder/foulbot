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
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClient _aiClient;
    private readonly IFoulChat _chat;
    private readonly CancellationTokenSource _cts;
    private readonly FoulBotConfiguration _config;

    private FoulBotNg(
        ChatScopedBotMessenger botMessenger,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        IFoulChat chat,
        CancellationTokenSource cts,
        FoulBotConfiguration config)
    {
        _botMessenger = botMessenger;
        _random = random;
        _aiClient = aiClient;
        _chat = chat;
        _cts = cts;
        _config = config;

        // Consider moving it outside (don't forget DisposeAsync).
        _chat.MessageReceived += OnMessageReceived;
    }

    public event EventHandler? BotFailed;

    public async ValueTask DisposeAsync()
    {
        _chat.MessageReceived -= OnMessageReceived;

        await _cts.CancelAsync();
        _cts.Dispose();
    }

    /// <summary>
    /// Returns null when it's not possible to join this bot to this chat.
    /// </summary>
    public static async ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);

        try
        {
            var canWriteToChat = await messenger.CheckCanWriteAsync();
            if (!canWriteToChat)
                throw new InvalidOperationException("Bot cannot write to chat.");

            // Do not create disposable bot instance unless it can write to chat.
            return new FoulBotNg(messenger, random, aiClient, chat, cts, config);
        }
        catch (InvalidOperationException)
        {
            cts.Dispose();
            return null;
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }

    public async ValueTask GreetEveryoneAsync(ChatParticipant invitedBy)
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

    private void OnMessageReceived(object? sender, FoulMessage message)
        => _ = OnMessageReceivedAsync(message);

    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        try
        {
            // Test code.
            await _botMessenger.SendTextMessageAsync(message.Text);
        }
        catch
        {
            // TODO: Consider returning obolean from all botMessenger operations
            // instead of relying on exceptions.
            BotFailed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }
}

public interface IFoulBotNgFactory
{
    ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config);
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

    public ValueTask<FoulBotNg?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        return FoulBotNg.JoinBotToChatAsync(
            botMessenger,
            _random,
            _aiClientFactory.Create(config.OpenAIModel),
            chat,
            config);
    }
}
