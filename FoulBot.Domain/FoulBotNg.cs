namespace FoulBot.Domain;

/// <summary>
/// Handles logic of processing messages and deciding whether to reply.
/// </summary>
public sealed class FoulBotNg : IFoulBotNg, IAsyncDisposable
{
    private readonly ChatScopedBotMessenger _botMessenger;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly IBotReplyStrategy _replyStrategy;
    private readonly ITypingImitatorFactory _typingImitatorFactory;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClient _aiClient;
    private readonly IMessageFilter _messageFilter;
    private readonly IFoulChat _chat;
    private readonly CancellationTokenSource _cts;
    private readonly FoulBotConfiguration _config;

    public FoulBotNg(
        ChatScopedBotMessenger botMessenger,
        IBotDelayStrategy delayStrategy,
        IBotReplyStrategy replyStrategy,
        ITypingImitatorFactory typingImitatorFactory,
        ISharedRandomGenerator random,
        IFoulAIClient aiClient,
        IMessageFilter messageFilter,
        IFoulChat chat,
        CancellationTokenSource cts,
        FoulBotConfiguration config)
    {
        _botMessenger = botMessenger;
        _delayStrategy = delayStrategy;
        _replyStrategy = replyStrategy;
        _typingImitatorFactory = typingImitatorFactory;
        _random = random;
        _aiClient = aiClient;
        _messageFilter = messageFilter;
        _chat = chat;
        _cts = cts;
        _config = config;
    }

    public FoulBotId BotId => _config.FoulBotId;
    public event EventHandler? BotFailed;

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

    public async Task TriggerAsync(FoulMessage message)
    {
        try
        {
            var context = _replyStrategy.GetContextForReplying(message);
            if (context == null)
                return;

            // TODO: Make sure only one TriggerAsync is executing at a time, at this point.
            // !! and do not just grab lock, CANCEL all other locks if any

            // Simulate "reading" the chat.
            await _delayStrategy.DelayAsync(_cts.Token);

            // TODO: pass isVoice.
            await using var typing = _typingImitatorFactory.ImitateTyping(_chat.ChatId, false);

            var i = 0;
            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);
            while (!_messageFilter.IsGoodMessage(aiGeneratedTextResponse) && i < 3)
            {
                i++;
                aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync([
                    new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false),
                    .. context
                ]);
            }

            await typing.FinishTypingText(aiGeneratedTextResponse);

            await _botMessenger.SendTextMessageAsync(aiGeneratedTextResponse);

            if (_messageFilter.IsGoodMessage(aiGeneratedTextResponse) || _config.IsAssistant)
            {
                _chat.AddMessage(new FoulMessage(
                    Guid.NewGuid().ToString(),
                    FoulMessageType.Bot,
                    _config.BotName,
                    aiGeneratedTextResponse,
                    DateTime.UtcNow, // TODO: Consider using timeprovider.
                    true));
            }
        }
        catch
        {
            // TODO: Consider returning obolean from all botMessenger operations
            // instead of relying on exceptions.
            BotFailed?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }
}
