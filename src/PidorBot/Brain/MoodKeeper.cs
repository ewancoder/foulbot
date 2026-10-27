using System.Collections.Concurrent;
using PidorBot.AI;
using PidorBot.Chats;
using PidorBot.Storage;

namespace PidorBot.Brain;

/// <summary>
/// The bot's mood in each chat. Changes every half an hour or so: the thinking model comes up with a new mood
/// based on what is going on in the chat, the hardcoded moods of the mode are the fallback.
/// Moods are refreshed in the background, so replies never wait for the thinking model.
/// </summary>
public sealed class MoodKeeper
{
    private const int MaxMoodLength = 400;
    private const int MessagesToConsider = 30;
    private static readonly TimeSpan _minPeriod = TimeSpan.FromMinutes(25);
    private static readonly TimeSpan _maxPeriod = TimeSpan.FromMinutes(40);

    private readonly ILogger<MoodKeeper> _logger;
    private readonly IChatStore _store;
    private readonly ILlm _llm;
    private readonly MemoryKeeper _memory;
    private readonly Settings _settings;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly ConcurrentDictionary<ChatRef, MoodState> _moods = new();
    private readonly ConcurrentDictionary<ChatRef, bool> _refreshing = new();

    public MoodKeeper(
        ILogger<MoodKeeper> logger,
        IChatStore store,
        ILlm llm,
        MemoryKeeper memory,
        Settings settings,
        TimeProvider time,
        Random random)
    {
        _logger = logger;
        _store = store;
        _llm = llm;
        _memory = memory;
        _settings = settings;
        _time = time;
        _random = random;
    }

    /// <summary>
    /// Current mood. When it's time for a new mood, the current one is still returned while the new one is being thought of.
    /// </summary>
    public async Task<string> GetAsync(
        ChatRef chat, BotMode mode, Func<IReadOnlyList<ChatMessage>> history, CancellationToken cancellationToken)
    {
        var mood = await PeekAsync(chat);
        var now = _time.GetUtcNow().UtcDateTime;

        if (mood == null || mood.Mode != mode.Name)
        {
            // First mood in this chat, or the mode has changed: pick a hardcoded one right away.
            mood = await SetAsync(chat, mode, PickFallback(mode));
            StartRefresh(chat, mode, history, cancellationToken);
        }
        else if (mood.ExpiresAt <= now)
        {
            StartRefresh(chat, mode, history, cancellationToken);
        }

        return mood.Text;
    }

    public async Task<MoodState?> PeekAsync(ChatRef chat)
    {
        if (_moods.TryGetValue(chat, out var mood))
            return mood;

        mood = await _store.GetMoodAsync(chat);
        return mood == null ? null : _moods.GetOrAdd(chat, mood);
    }

    /// <summary>
    /// Thinks of a new mood right away ($newmood command).
    /// </summary>
    public async Task<string> RenewAsync(
        ChatRef chat, BotMode mode, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        var text = await ThinkOfMoodAsync(chat, mode, history, cancellationToken);
        return (await SetAsync(chat, mode, text)).Text;
    }

    private void StartRefresh(
        ChatRef chat, BotMode mode, Func<IReadOnlyList<ChatMessage>> history, CancellationToken cancellationToken)
    {
        if (!_refreshing.TryAdd(chat, true))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var text = await ThinkOfMoodAsync(chat, mode, history(), cancellationToken);
                await SetAsync(chat, mode, text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not refresh mood in chat {ChatId}", chat);
            }
            finally
            {
                _refreshing.TryRemove(chat, out _);
            }
        }, CancellationToken.None);
    }

    private async Task<string> ThinkOfMoodAsync(
        ChatRef chat, BotMode mode, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        if (!_settings.LlmMoods)
            return PickFallback(mode);

        try
        {
            var memory = await _memory.GetAsync(chat);
            var previous = await PeekAsync(chat);
            var localNow = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _settings.TimeZone);

            var prompt = Prompts.MoodGeneration(
                mode,
                memory?.Text,
                [.. history.TakeLast(MessagesToConsider)],
                previous?.Mode == mode.Name ? previous.Text : null,
                localNow,
                _settings.TimeZone);

            var mood = Sanitize(await _llm.CompleteTextAsync(prompt, LlmTier.Thinking, cancellationToken));
            if (mood != null)
            {
                _logger.LogInformation("New mood in chat {ChatId} ({Mode}): {Mood}", chat, mode.Name, mood);
                return mood;
            }

            _logger.LogWarning("Thinking model came up with a bad mood, using a hardcoded one");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "Could not think of a mood, using a hardcoded one");
        }

        return PickFallback(mode);
    }

    private async Task<MoodState> SetAsync(ChatRef chat, BotMode mode, string text)
    {
        var period = _minPeriod + ((_maxPeriod - _minPeriod) * _random.NextDouble());
        var mood = new MoodState(mode.Name, text, _time.GetUtcNow().UtcDateTime + period);

        _moods[chat] = mood;
        await _store.SetMoodAsync(chat, mood);

        return mood;
    }

    private string PickFallback(BotMode mode) => mode.Moods[_random.Next(mode.Moods.Count)];

    internal static string? Sanitize(string mood)
    {
        mood = mood.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        mood = mood.Trim('"', '«', '»', ' ');
        if (mood.StartsWith("Your mood right now:", StringComparison.OrdinalIgnoreCase))
            mood = mood["Your mood right now:".Length..].Trim();

        if (mood.Length == 0 || mood.Length > MaxMoodLength || !ReplyFilter.IsGood(mood))
            return null;

        return mood.EndsWith('.') || mood.EndsWith('!') ? mood : $"{mood}.";
    }
}
