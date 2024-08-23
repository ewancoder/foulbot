namespace FoulBot.Domain;

public interface IReplyImitatorFactory
{
    IReplyImitator ImitateTyping(FoulChatId chatId, BotReplyMode replyMode);
}

public sealed class TypingImitatorFactory : IReplyImitatorFactory
{
    private readonly ILogger<ReplyImitator> _logger;
    private readonly IBotMessenger _botMessenger;
    private readonly TimeProvider _timeProvider;
    private readonly ISharedRandomGenerator _random;

    public TypingImitatorFactory(
        ILogger<ReplyImitator> logger,
        IBotMessenger botMessenger,
        TimeProvider timeProvider,
        ISharedRandomGenerator random)
    {
        _logger = logger;
        _botMessenger = botMessenger;
        _timeProvider = timeProvider;
        _random = random;
    }

    public IReplyImitator ImitateTyping(FoulChatId chatId, BotReplyMode replyMode)
    {
        return new ReplyImitator(
            _logger, _botMessenger, _timeProvider, _random, chatId, replyMode);
    }
}

// HACK: Antipattern, but I need IDisposable on this interface for now.
public interface IReplyImitator : IAsyncDisposable
{
    ValueTask FinishReplyingAsync(string text);
}

/// <summary>
/// Imitates an action such as Typing or Recording voice.
/// Disposing of it stops the action.
/// </summary>
public sealed class ReplyImitator : IReplyImitator, IAsyncDisposable
{
    private const int MinRandomWaitMs = 1500;
    private const int MaxRandomWaitMs = 10000;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<ReplyImitator> _logger;
    private readonly IBotMessenger _messenger;
    private readonly TimeProvider _timeProvider;
    private readonly ISharedRandomGenerator _random;
    private readonly FoulChatId _chatId;
    private readonly BotReplyMode _replyMode;
    private readonly DateTime _startedAt;
    private readonly Task _typing;
    private string? _text;

    public ReplyImitator(
        ILogger<ReplyImitator> logger,
        IBotMessenger messenger,
        TimeProvider timeProvider,
        ISharedRandomGenerator random,
        FoulChatId chatId,
        BotReplyMode replyMode)
    {
        _logger = logger;
        _messenger = messenger;
        _timeProvider = timeProvider;
        _random = random;
        _chatId = chatId;
        _replyMode = replyMode;

        _startedAt = _timeProvider.GetUtcNow().UtcDateTime;
        _typing = ImitateReplyingAsync();
    }

    private async Task ImitateReplyingAsync()
    {
        while (_text == null)
        {
            _logger.LogDebug("Starting typing imitation with {@ReplyMode}", _replyMode);

            if (_timeProvider.GetUtcNow().UtcDateTime - _startedAt > TimeSpan.FromMinutes(1))
            {
                _logger.LogDebug("Typing imitation was going on for 1 minute, stopping it.");
                return; // If we're typing for 1 minute straight - stop it.
            }

            if (_replyMode.Type == ReplyType.Voice)
            {
                _logger.LogDebug("Notify messenger about voice recording.");
                await _messenger.NotifyRecordingVoiceAsync(_chatId);
            }
            else
            {
                _logger.LogDebug("Notify messenger about typing.");
                await _messenger.NotifyTyping(_chatId);
            }

            try
            {
                var randomValue = _random.Generate(MinRandomWaitMs, MaxRandomWaitMs);
                _logger.LogDebug("Waiting random amount {Amount} milliseconds, or till imitator is canceled.", randomValue);

                await Task.Delay(
                    TimeSpan.FromMilliseconds(randomValue),
                    _timeProvider,
                    _cts.Token);
            }
            catch (TaskCanceledException)
            {
                _logger.LogDebug("Imitator has been canceled. Stopping imitation.");
                return;
            }
        }

        var requiredTimeToTypeText = TimeSpan.FromSeconds(Convert.ToInt32(Math.Floor(
            60m * (_text.Length / 1000m))));

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var remainingMilliseconds = (requiredTimeToTypeText - (now - _startedAt)).TotalMilliseconds;
        while (remainingMilliseconds > 1000)
        {
            _logger.LogDebug("We still need to imitate typing the rest of the text. Remaining milliseconds is {RemainingMilliseconds}.", remainingMilliseconds);

            remainingMilliseconds = (requiredTimeToTypeText - (now - _startedAt)).TotalMilliseconds;
            if (remainingMilliseconds <= 0)
            {
                _logger.LogDebug("Actually no, we're ok to finish here. Remaining milliseconds is {RemainingMilliseconds}.", remainingMilliseconds);
                break;
            }

            if (_replyMode.Type == ReplyType.Voice)
            {
                _logger.LogDebug("Notify messenger about voice recording.");
                await _messenger.NotifyRecordingVoiceAsync(_chatId);
            }
            else
            {
                _logger.LogDebug("Notify messenger about typing.");
                await _messenger.NotifyTyping(_chatId);
            }

            var randomValue = _random.Generate(MinRandomWaitMs, MaxRandomWaitMs);
            _logger.LogDebug("Waiting random amount {Amount} milliseconds, or {RemainingMilliseconds} milliseconds, whichever is smaller.", randomValue, remainingMilliseconds);
            var waitMilliseconds = Convert.ToInt32(Math.Floor(Math.Min(randomValue, remainingMilliseconds)));
            await Task.Delay(waitMilliseconds);
        }

        _logger.LogDebug("Stopped typing imitation.");
    }

    public async ValueTask FinishReplyingAsync(string text)
    {
        _text = text; // This stops the while loop that sends typing events.
        await _cts.CancelAsync();
        await _typing;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await FinishReplyingAsync(string.Empty);
            _cts.Dispose();
        }
        catch (TaskCanceledException exception)
        {
            _logger.LogWarning(exception, "Exception when trying to dispose TypingImitator.");
        }
        catch (ObjectDisposedException exception)
        {
            _logger.LogWarning(exception, "Exception when trying to dispose TypingImitator.");
        }
    }
}
