using PidorBot.Chats;

namespace PidorBot.Storage;

/// <param name="Mode">Mode the mood was picked for: switching the mode changes the mood.</param>
public sealed record MoodState(string Mode, string Text, DateTime ExpiresAt);

/// <param name="CoveredUntil">Date of the last message that is already in the memory.</param>
public sealed record ChatMemory(string Text, DateTime UpdatedAt, DateTime CoveredUntil);

/// <summary>
/// Everything the bot remembers about chats. Implementations should not throw:
/// the bot keeps working (with less memory) when the storage is down.
/// </summary>
public interface IChatStore
{
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ChatRef chat, int count);
    Task AddToHistoryAsync(ChatRef chat, ChatMessage message);

    Task<string?> GetModeAsync(ChatRef chat);
    Task SetModeAsync(ChatRef chat, string mode);

    Task<MoodState?> GetMoodAsync(ChatRef chat);
    Task SetMoodAsync(ChatRef chat, MoodState mood);

    Task<ChatMemory?> GetMemoryAsync(ChatRef chat);
    Task SetMemoryAsync(ChatRef chat, ChatMemory? memory);

    Task<IReadOnlyList<string>> GetDirectivesAsync(ChatRef chat);
    Task SetDirectivesAsync(ChatRef chat, IReadOnlyList<string> directives);
}

/// <summary>
/// Custom names of people set in the old FoulBot, by Telegram username.
/// </summary>
public interface INameStore
{
    Task<string?> GetNameAsync(string username);
}
