namespace FoulBot.Domain;

public interface IFoulBot
{
    event EventHandler? Shutdown;

    Task GracefulShutdownAsync();
}

/// <summary>
/// Handles logic of processing messages and deciding whether to reply.
/// </summary>
public sealed class FoulBot : IFoulBot, IAsyncDisposable
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
    private int _triggerCalls;
    private bool _isShuttingDown;

    public FoulBot(
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

    public event EventHandler? Shutdown;

    public async ValueTask GreetEveryoneAsync(ChatParticipant invitedBy)
    {
        if (_config.Stickers.Count != 0)
        {
            var stickerIndex = _random.Generate(0, _config.Stickers.Count - 1);
            var stickerId = _config.Stickers[stickerIndex];

            await _botMessenger.SendStickerAsync(stickerId);
        }

        var directive = $"{_config.Directive}. You have just been added to a chat group with a number of people by a person named {invitedBy.Name}, tell them hello in your manner or thank the person for adding you if you feel like it.";
        var greetingsMessage = await _aiClient.GetCustomResponseAsync(directive); // TODO: Pass cancellation token.
        await _botMessenger.SendTextMessageAsync(greetingsMessage);
    }

    public async ValueTask TriggerAsync(FoulMessage message)
    {
        var value = Interlocked.Increment(ref _triggerCalls);
        try
        {
            if (value > 1) return;

            var context = _replyStrategy.GetContextForReplying(message);
            if (context == null)
                return;

            // Simulate "reading" the chat.
            await _delayStrategy.DelayAsync(_cts.Token);

            // TODO: pass isVoice.
            await using var typing = _typingImitatorFactory.ImitateTyping(_chat.ChatId, false);

            // TODO: Consider moving retry logic to a separate class.
            // It is untested for now.
            var i = 0;
            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context); // TODO: Pass cancellation token.
            while (!_messageFilter.IsGoodMessage(aiGeneratedTextResponse) && i < 3)
            {
                i++;
                aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync([ // TODO: Pass cancellation token.
                    new FoulMessage("Directive", FoulMessageType.System, "System", _config.Directive, DateTime.MinValue, false, null),
                    .. context
                ]);
            }

            await typing.FinishTypingText(aiGeneratedTextResponse); // TODO: Pass cancellation token.

            await _botMessenger.SendTextMessageAsync(aiGeneratedTextResponse); // TODO: Pass cancellation token.

            if (_messageFilter.IsGoodMessage(aiGeneratedTextResponse) || _config.IsAssistant)
            {
                _chat.AddMessage(new FoulMessage(
                    Guid.NewGuid().ToString(),
                    FoulMessageType.Bot,
                    _config.BotName,
                    aiGeneratedTextResponse,
                    DateTime.UtcNow, // TODO: Consider using timeprovider.
                    true,
                    null));
            }
        }
        catch
        {
            // TODO: Consider returning boolean from all botMessenger operations
            // instead of relying on exceptions.
            await GracefulShutdownAsync();
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _triggerCalls);
        }
    }

    /// <summary>
    /// Cancels internal operations and fires Shutdown event.
    /// </summary>
    public async Task GracefulShutdownAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        await _cts.CancelAsync();
        Shutdown?.Invoke(this, EventArgs.Empty); // Class that subscribes to this event should dispose of this FoulBot instance.
    }

    /// <summary>
    /// Cancels internal operations and disposes of resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_isShuttingDown)
            await GracefulShutdownAsync();

        _cts.Dispose();
    }
}
