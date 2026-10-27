using System.Collections.Concurrent;
using PidorBot.AI;
using PidorBot.Chats;
using PidorBot.Storage;

namespace PidorBot.Brain;

/// <summary>
/// Long-term memory of each chat: who the people are, what they've been talking about, the vibe of the chat,
/// and what the bot has said about itself. The thinking model rewrites it in the background every few dozen messages.
/// </summary>
public sealed class MemoryKeeper : IDisposable
{
    public const int MessagesForFirstMemory = 10;
    public const int MessagesForUpdate = 40;
    public const int MessagesForLateUpdate = 8;
    public static readonly TimeSpan LateUpdateAfter = TimeSpan.FromHours(4);

    private const int MaxMessagesPerUpdate = 200;
    private const string RequiredSection = "ЛЮДИ";
    private static readonly TimeSpan _retryAfterFailure = TimeSpan.FromMinutes(30);

    private readonly ILogger<MemoryKeeper> _logger;
    private readonly IChatStore _store;
    private readonly ILlm _llm;
    private readonly Settings _settings;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<ChatRef, ChatMemory?> _memories = new();
    private readonly ConcurrentDictionary<ChatRef, DateTime> _retryAfter = new();
    private readonly ConcurrentDictionary<ChatRef, bool> _updating = new();

    // Don't think about all the chats at once after startup.
    private readonly SemaphoreSlim _concurrency = new(2, 2);

    public MemoryKeeper(
        ILogger<MemoryKeeper> logger,
        IChatStore store,
        ILlm llm,
        Settings settings,
        TimeProvider time)
    {
        _logger = logger;
        _store = store;
        _llm = llm;
        _settings = settings;
        _time = time;
    }

    public async Task<ChatMemory?> GetAsync(ChatRef chat)
    {
        if (_memories.TryGetValue(chat, out var memory))
            return memory;

        memory = await _store.GetMemoryAsync(chat);
        return _memories.GetOrAdd(chat, memory);
    }

    public async Task ForgetAsync(ChatRef chat)
    {
        _memories[chat] = null;
        await _store.SetMemoryAsync(chat, null);
    }

    /// <summary>
    /// Updates the memory in the background if enough has happened in the chat.
    /// </summary>
    public void Consider(ChatRef chat, Func<IReadOnlyList<ChatMessage>> history, CancellationToken cancellationToken)
    {
        if (!_settings.Memory
            || (_retryAfter.TryGetValue(chat, out var retryAfter) && _time.GetUtcNow().UtcDateTime < retryAfter)
            || !_updating.TryAdd(chat, true))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateIfNeededAsync(chat, history(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not update memory of chat {ChatId}", chat);
                _retryAfter[chat] = _time.GetUtcNow().UtcDateTime + _retryAfterFailure;
            }
            finally
            {
                _updating.TryRemove(chat, out _);
            }
        }, CancellationToken.None);
    }

    internal static bool ShouldUpdate(ChatMemory? memory, int newMessages, DateTime now) => memory == null
        ? newMessages >= MessagesForFirstMemory
        : newMessages >= MessagesForUpdate
            || (newMessages >= MessagesForLateUpdate && now - memory.UpdatedAt >= LateUpdateAfter);

    private async Task UpdateIfNeededAsync(
        ChatRef chat, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken)
    {
        var memory = await GetAsync(chat);
        var newMessages = history
            .Where(message => memory == null || message.Date > memory.CoveredUntil)
            .TakeLast(MaxMessagesPerUpdate)
            .ToList();

        if (!ShouldUpdate(memory, newMessages.Count, _time.GetUtcNow().UtcDateTime))
            return;

        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Updating memory of chat {ChatId} with {Count} new messages", chat, newMessages.Count);

            var prompt = Prompts.MemoryUpdate(memory?.Text, newMessages, _settings.TimeZone);
            var text = await _llm.CompleteTextAsync(prompt, LlmTier.Thinking, cancellationToken);

            // Refusals and garbage don't have the sections.
            if (!text.Contains(RequiredSection, StringComparison.Ordinal))
                throw new InvalidOperationException($"Thinking model returned a bad memory: {text}");

            var updated = new ChatMemory(Truncate(text.Trim()), _time.GetUtcNow().UtcDateTime, newMessages[^1].Date);
            _memories[chat] = updated;
            await _store.SetMemoryAsync(chat, updated);

            _logger.LogInformation("Updated memory of chat {ChatId}: {Memory}", chat, updated.Text);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public void Dispose() => _concurrency.Dispose();

    private static string Truncate(string text)
    {
        // Some slack: the model doesn't count characters precisely.
        var limit = Prompts.MaxMemoryCharacters * 3 / 2;
        if (text.Length <= limit)
            return text;

        var cut = text.LastIndexOf('\n', limit);
        return text[..(cut > 0 ? cut : limit)];
    }
}
