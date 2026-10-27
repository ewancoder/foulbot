namespace PidorBot.Chats;

/// <summary>
/// Makes the bot look like a person: it takes time to read the chat and to type a reply.
/// </summary>
public interface IPacing
{
    Task ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Shows "typing..." (or "recording voice...") until the returned object is completed or disposed.
    /// </summary>
    ITyping StartTyping(ChatRef chat, bool isVoice, CancellationToken cancellationToken);
}

public interface ITyping : IAsyncDisposable
{
    /// <summary>
    /// Keeps typing until a person would have typed this text.
    /// </summary>
    Task FinishAsync(string text, CancellationToken cancellationToken);
}

public sealed class HumanPacing : IPacing
{
    private readonly ILogger<HumanPacing> _logger;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;
    private readonly Random _random;

    public HumanPacing(ILogger<HumanPacing> logger, IMessenger messenger, TimeProvider time, Random random)
    {
        _logger = logger;
        _messenger = messenger;
        _time = time;
        _random = random;
    }

    public Task ReadAsync(CancellationToken cancellationToken)
    {
        // Usually the bot reads right away, sometimes it takes a while.
        var chance = _random.Next(100);
        var delay = chance switch
        {
            < 70 => _random.Next(200, 1200),
            < 90 => _random.Next(1500, 5000),
            _ => _random.Next(5000, 20000)
        };

        _logger.LogDebug("Reading the chat for {Delay} ms", delay);
        return Task.Delay(TimeSpan.FromMilliseconds(delay), _time, cancellationToken);
    }

    public ITyping StartTyping(ChatRef chat, bool isVoice, CancellationToken cancellationToken)
        => new Typing(_logger, _messenger, _time, chat, isVoice, cancellationToken);

    private sealed class Typing : ITyping
    {
        // Telegram shows the typing status for 5 seconds.
        private static readonly TimeSpan _notifyEvery = TimeSpan.FromSeconds(4);
        private static readonly TimeSpan _typingPerCharacter = TimeSpan.FromMilliseconds(30);
        private static readonly TimeSpan _maxTyping = TimeSpan.FromMinutes(1);

        private readonly ILogger _logger;
        private readonly IMessenger _messenger;
        private readonly TimeProvider _time;
        private readonly ChatRef _chat;
        private readonly bool _isVoice;
        private readonly DateTimeOffset _startedAt;
        private readonly CancellationTokenSource _cts;
        private readonly Task _notifying;

        public Typing(
            ILogger logger, IMessenger messenger, TimeProvider time, ChatRef chat, bool isVoice, CancellationToken cancellationToken)
        {
            _logger = logger;
            _messenger = messenger;
            _time = time;
            _chat = chat;
            _isVoice = isVoice;
            _startedAt = time.GetUtcNow();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _notifying = NotifyAsync(_cts.Token);
        }

        public async Task FinishAsync(string text, CancellationToken cancellationToken)
        {
            var typingTime = _typingPerCharacter * text.Length;
            if (typingTime > _maxTyping)
                typingTime = _maxTyping;

            var remaining = typingTime - (_time.GetUtcNow() - _startedAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, _time, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _notifying;
            _cts.Dispose();
        }

        private async Task NotifyAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _time.GetUtcNow() - _startedAt < _maxTyping * 2)
                {
                    await _messenger.NotifyTypingAsync(_chat, _isVoice, cancellationToken);
                    await Task.Delay(_notifyEvery, _time, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Could not notify about typing");
            }
        }
    }
}
