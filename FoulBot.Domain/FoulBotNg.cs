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
    //private readonly ISharedRandomGenerator _random;
    //private readonly IFoulAIClient _aiClient;
    private readonly CancellationTokenSource _cts;
    //private readonly FoulBotConfiguration _config;

    private FoulBotNg(
        ChatScopedBotMessenger botMessenger,
        IFoulChat chat,
        //ISharedRandomGenerator random,
        //IFoulAIClient aiClient,
        //FoulBotConfiguration config,
        CancellationTokenSource cts)
    {
        _botMessenger = botMessenger;
        _chat = chat;
        //_random = random;
        //_aiClient = aiClient;
        //_config = config;
        _cts = cts;

        // Consider moving it outside (don't forget DisposeAsync).
        _chat.MessageReceived += OnMessageReceived;
    }

    public async ValueTask DisposeAsync()
    {
        _chat.MessageReceived -= OnMessageReceived;

        await _cts.CancelAsync();
        _cts.Dispose();
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
            throw new InvalidOperationException("Bot cannot write to invited chat.");

        await GreetEveryoneAsync(messenger, aiClient, config, random, invitedBy);

        // Do not create disposable bot instance unless it can write to chat.
        //return new FoulBotNg(messenger, chat, random, aiClient, config, cts);
        return new FoulBotNg(messenger, chat, cts);
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
    ValueTask<FoulBotNg> InviteBotToChatAsync(
        FoulBotConfiguration config, ChatParticipant invitedBy);
}

public sealed class FoulBotNgFactory : IFoulBotNgFactory
{
    private readonly IBotMessenger _botMessenger;
    private readonly IFoulChat _chat;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClient _aiClient;

    public FoulBotNgFactory(
        IBotMessenger botMessenger,
        IFoulChat chat,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient)
    {
        _botMessenger = botMessenger;
        _chat = chat;
        _random = random;
        _aiClient = aiClient;
    }

    public ValueTask<FoulBotNg> InviteBotToChatAsync(
        FoulBotConfiguration config, ChatParticipant invitedBy)
    {
        return FoulBotNg.InviteBotToChatAsync(
            _botMessenger,
            _chat,
            _random,
            _aiClient,
            config,
            invitedBy);
    }
}
