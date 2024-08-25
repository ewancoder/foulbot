namespace FoulBot.Domain;

public sealed class TalkYourselfFeature : IBotFeature, IAsyncDisposable
{
    public const int MinDelayTimeMinutes = 6 * 60;
    public const int MaxDelayTimeMinutes = 24 * 60;
    private readonly ILogger<TalkYourselfFeature> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulBot _bot;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _botId;
    private bool _isDisabled;
    private bool _isStopping;

    // TODO: Pass cancellationToken from the bot here? Or maybe calling StopFeatureAsync from the bot is enough.
    public TalkYourselfFeature(
        ILogger<TalkYourselfFeature> logger,
        TimeProvider timeProvider,
        ISharedRandomGenerator random,
        IFoulBot bot,
        FoulBotConfiguration config)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _random = random;
        _bot = bot;
        _botId = config.BotId;

        _ = TalkAsync();
    }

    public ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        var text = message.Text.Replace($"@{_botId}", string.Empty).Trim();

        if (text == "/talkyourself disable")
        {
            _isDisabled = true;
            return new(true);
        }

        if (text == "/talkyourself enable")
        {
            _isDisabled = false;
            return new(true);
        }

        return new(false);
    }

    public async ValueTask StopFeatureAsync()
    {
        _isStopping = true;

        await _cts.CancelAsync();
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isStopping)
            await StopFeatureAsync();

        _cts.Dispose();
    }

    private async Task TalkAsync()
    {
        while (true)
        {
            var delayMinutes = _random.Generate(MinDelayTimeMinutes, MaxDelayTimeMinutes);

            _logger.LogDebug("Waiting for {DelayMinutes} minutes until sending a message", delayMinutes);
            await Task.Delay(TimeSpan.FromMinutes(delayMinutes), _timeProvider, _cts.Token);

            if (_isDisabled)
            {
                _logger.LogDebug("TalkYourself feature is disabled. Skipping sending a message");
                continue;
            }

            _logger.LogDebug("Triggering a reply from bot by TalkYourself feature");

            await _bot.TriggerAsync(new FoulMessage(
                Guid.NewGuid().ToString(),
                FoulMessageType.User,
                new("TalkYourself"),
                string.Empty,
                _timeProvider.GetUtcNow().UtcDateTime,
                false,
                _botId)
            {
                ForceReply = true
            });
        }
    }
}
