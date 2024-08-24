namespace FoulBot.Domain;

public interface IBotDelayStrategy
{
    /// <summary>
    /// Delays processing message, creating a fictional "reading messages" pause.
    /// </summary>
    ValueTask DelayAsync(CancellationToken cancellationToken);
}

public sealed class BotDelayStrategy : IBotDelayStrategy
{
    private readonly ILogger<BotDelayStrategy> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly TimeProvider _timeProvider;

    public BotDelayStrategy(
        ILogger<BotDelayStrategy> logger,
        ISharedRandomGenerator random,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _random = random;
        _timeProvider = timeProvider;
    }

    public async ValueTask DelayAsync(CancellationToken cancellationToken)
    {
        var delay = _random.Generate(1, 100);
        if (delay > 90)
        {
            delay = _random.Generate(5000, 20000);
        }
        if (delay <= 90 && delay > 70)
        {
            delay = _random.Generate(1500, 5000);
        }
        if (delay <= 70)
        {
            delay = _random.Generate(200, 1200);
        }

        _logger.LogDebug("Initiating artificial delay of {Delay} milliseconds to read the message with \"Bot's eyes\".", delay);
        await Task.Delay(TimeSpan.FromMilliseconds(delay), _timeProvider, cancellationToken);
    }
}
