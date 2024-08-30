using FoulBot.Domain.Connections;

namespace FoulBot.Domain;

public interface IReplyImitatorFactory
{
    IReplyImitator ImitateReplying(FoulChatId chatId, BotReplyMode replyMode);
}

public sealed class ReplyImitatorFactory : IReplyImitatorFactory
{
    private readonly ILogger<ReplyImitator> _logger;
    private readonly IBotMessenger _botMessenger;
    private readonly TimeProvider _timeProvider;
    private readonly ISharedRandomGenerator _random;

    public ReplyImitatorFactory(
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

    public IReplyImitator ImitateReplying(FoulChatId chatId, BotReplyMode replyMode)
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
    public const int MinRandomWaitMs = 1500;
    public const int MaxRandomWaitMs = 10000;
    private readonly CancellationTokenSource _shutdownCts = new();
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

    // TODO: Unit test not covered parts, and less than 1 second check.
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
                break;
            }
        }

        _text ??= string.Empty;

        var requiredTimeToTypeText = TimeSpan.FromMilliseconds(Convert.ToInt32(Math.Floor(
            30m * _text.Length)));

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var remainingMilliseconds = (requiredTimeToTypeText - (now - _startedAt)).TotalMilliseconds;
        while (remainingMilliseconds > 1000)
        {
            _logger.LogDebug("We still need to imitate typing the rest of the text. Remaining milliseconds is {RemainingMilliseconds}.", remainingMilliseconds);

            now = _timeProvider.GetUtcNow().UtcDateTime;
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
            await Task.Delay(TimeSpan.FromMilliseconds(waitMilliseconds), _timeProvider, _shutdownCts.Token); // TODO: Add a different cancellation token here that will work on app shutdown.
        }

        _logger.LogDebug("Stopped typing imitation.");
    }

    public async ValueTask FinishReplyingAsync(string text)
    {
        _text = text; // This stops the while loop that sends typing events.
        await _cts.CancelAsync();
        await _typing;
    }

    // TODO: Unit test that Dispose doesn't wait to finish typing, it stops straightaway.
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _shutdownCts.CancelAsync();
            await FinishReplyingAsync(string.Empty);
            _cts.Dispose();
            _shutdownCts.Dispose();
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
