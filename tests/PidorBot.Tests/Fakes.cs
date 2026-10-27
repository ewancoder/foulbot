using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PidorBot.AI;
using PidorBot.Brain;
using PidorBot.Chats;
using PidorBot.Reminders;
using PidorBot.Storage;

namespace PidorBot.Tests;

public sealed class FakeLlm : ILlm
{
    private readonly Queue<LlmResponse> _responses = new();

    public List<(IReadOnlyList<LlmMessage> Messages, LlmTier Tier, IReadOnlyList<LlmTool> Tools)> Calls { get; } = [];

    /// <summary>
    /// Returned when there are no queued responses.
    /// </summary>
    public string DefaultText { get; set; } = "Чего тебе, пидор?";

    public FakeLlm Respond(string text, params LlmToolCall[] toolCalls)
    {
        _responses.Enqueue(new(text, toolCalls));
        return this;
    }

    public Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, LlmTier tier, IReadOnlyList<LlmTool> tools, CancellationToken cancellationToken)
    {
        Calls.Add(([.. messages], tier, tools));
        return Task.FromResult(_responses.TryDequeue(out var response) ? response : new LlmResponse(DefaultText, []));
    }
}

public sealed class FakeMessenger : IMessenger
{
    public List<string> Texts { get; } = [];
    public List<string> Stickers { get; } = [];
    public List<(int MessageId, string Emoji)> Reactions { get; } = [];
    public List<string?> Photos { get; } = [];
    public int Voices { get; private set; }

    public Task SendTextAsync(ChatRef chat, string text, CancellationToken cancellationToken)
    {
        Texts.Add(text);
        return Task.CompletedTask;
    }

    public Task SendVoiceAsync(ChatRef chat, Stream voice, CancellationToken cancellationToken)
    {
        Voices++;
        return Task.CompletedTask;
    }

    public Task SendStickerAsync(ChatRef chat, string stickerId, CancellationToken cancellationToken)
    {
        Stickers.Add(stickerId);
        return Task.CompletedTask;
    }

    public Task SendPhotoAsync(ChatRef chat, byte[] photo, string? caption, CancellationToken cancellationToken)
    {
        Photos.Add(caption);
        return Task.CompletedTask;
    }

    public Task ReactAsync(ChatRef chat, int messageId, string emoji, CancellationToken cancellationToken)
    {
        Reactions.Add((messageId, emoji));
        return Task.CompletedTask;
    }

    public Task NotifyTypingAsync(ChatRef chat, bool isVoice, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<byte[]> DownloadAsync(string fileId, CancellationToken cancellationToken) => Task.FromResult(new byte[] { 1, 2, 3 });
}

public sealed class InMemoryChatStore : IChatStore
{
    public Dictionary<ChatRef, List<ChatMessage>> History { get; } = [];
    public Dictionary<ChatRef, string> Modes { get; } = [];
    public Dictionary<ChatRef, MoodState> Moods { get; } = [];
    public Dictionary<ChatRef, ChatMemory?> Memories { get; } = [];
    public Dictionary<ChatRef, List<string>> Directives { get; } = [];

    public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(ChatRef chat, int count)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(History.TryGetValue(chat, out var list) ? [.. list.TakeLast(count)] : []);

    public Task AddToHistoryAsync(ChatRef chat, ChatMessage message)
    {
        if (!History.TryGetValue(chat, out var list))
            History[chat] = list = [];

        list.Add(message);
        return Task.CompletedTask;
    }

    public Task<string?> GetModeAsync(ChatRef chat) => Task.FromResult(Modes.TryGetValue(chat, out var mode) ? mode : null);

    public Task SetModeAsync(ChatRef chat, string mode)
    {
        Modes[chat] = mode;
        return Task.CompletedTask;
    }

    public Task<MoodState?> GetMoodAsync(ChatRef chat) => Task.FromResult(Moods.TryGetValue(chat, out var mood) ? mood : null);

    public Task SetMoodAsync(ChatRef chat, MoodState mood)
    {
        Moods[chat] = mood;
        return Task.CompletedTask;
    }

    public Task<ChatMemory?> GetMemoryAsync(ChatRef chat) => Task.FromResult(Memories.TryGetValue(chat, out var memory) ? memory : null);

    public Task SetMemoryAsync(ChatRef chat, ChatMemory? memory)
    {
        Memories[chat] = memory;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetDirectivesAsync(ChatRef chat)
        => Task.FromResult<IReadOnlyList<string>>(Directives.TryGetValue(chat, out var directives) ? directives : []);

    public Task SetDirectivesAsync(ChatRef chat, IReadOnlyList<string> directives)
    {
        Directives[chat] = [.. directives];
        return Task.CompletedTask;
    }
}

public sealed class FakeStickers : IStickerBook
{
    public Dictionary<ChatRef, List<KnownSticker>> Known { get; } = [];

    public Task<bool> KnowsSetAsync(ChatRef chat, string set) => Task.FromResult(Of(chat).Any(sticker => sticker.Set == set));

    public Task LearnAsync(ChatRef chat, IReadOnlyCollection<KnownSticker> stickers)
    {
        Of(chat).AddRange(stickers);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetEmojisAsync(ChatRef chat, int count)
        => Task.FromResult<IReadOnlyList<string>>([.. Of(chat).Select(sticker => sticker.Emoji).Distinct().Take(count)]);

    public Task<KnownSticker?> PickAsync(ChatRef chat, string emoji)
        => Task.FromResult(Of(chat).FirstOrDefault(sticker => sticker.Emoji == emoji) ?? Of(chat).FirstOrDefault());

    public List<KnownSticker> Of(ChatRef chat)
        => Known.TryGetValue(chat, out var stickers) ? stickers : Known[chat] = [];
}

public sealed class FakeImages : IImageGenerator
{
    public List<string> Prompts { get; } = [];

    public Task<byte[]?> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return Task.FromResult<byte[]?>([1, 2, 3]);
    }
}

public sealed class FakeVoice(bool isEnabled = false) : IVoice
{
    public bool IsEnabled => isEnabled;

    public Task<Stream?> SpeakAsync(string text, CancellationToken cancellationToken)
        => Task.FromResult<Stream?>(new MemoryStream([1]));
}

public sealed class NoPacing : IPacing
{
    public Task ReadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITyping StartTyping(ChatRef chat, bool isVoice, CancellationToken cancellationToken) => new NoTyping();

    private sealed class NoTyping : ITyping
    {
        public Task FinishAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }
}

/// <summary>
/// Everything the bot needs, faked. Disposes of the temporary reminders folder.
/// </summary>
public sealed class TestBot : IDisposable
{
    public static readonly BotIdentity Identity = new(42, "ew_pidorbot");
    public static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pidorbot-tests-{Guid.NewGuid()}");

    public TestBot(double glanceChance = 0, int voiceEveryReplies = 0)
    {
        Directory.CreateDirectory(_directory);
        Settings = new Settings
        {
            TelegramBotToken = "token",
            OpenAIKey = "key",
            RedisConnectionString = "localhost",
            LlmMoods = false,
            Memory = false,
            GlanceChance = glanceChance,
            VoiceEveryReplies = voiceEveryReplies,
            TimeZone = TimeZoneInfo.Utc,
            DataDirectory = _directory
        };

        Reminders = new ReminderBook(NullLogger<ReminderBook>.Instance, Path.Combine(_directory, "reminders"), Identity.Username);
        var memory = new MemoryKeeper(NullLogger<MemoryKeeper>.Instance, Store, Llm, Settings, Time);
        Services = new ChatServices(
            NullLoggerFactory.Instance,
            Messenger,
            Store,
            new Responder(NullLogger<Responder>.Instance, Llm, new Random(1)),
            new MoodKeeper(NullLogger<MoodKeeper>.Instance, Store, Llm, memory, Settings, Time, new Random(1)),
            memory,
            Reminders,
            Stickers,
            Images,
            new NoPacing(),
            new FakeVoice(voiceEveryReplies > 0),
            Settings,
            Identity,
            Time,
            new Random(1));
    }

    public FakeLlm Llm { get; } = new();
    public FakeMessenger Messenger { get; } = new();
    public InMemoryChatStore Store { get; } = new();
    public FakeStickers Stickers { get; } = new();
    public FakeImages Images { get; } = new();
    public FakeTimeProvider Time { get; } = new(Start);
    public Settings Settings { get; }
    public ReminderBook Reminders { get; }
    public ChatServices Services { get; }

    public static ChatMessage Message(string text, int telegramId = 1, string sender = "Ivan_Petrov", bool isReplyToBot = false)
        => new($"tg-{telegramId}", sender, "ivan", text, Start.UtcDateTime, IsFromBot: false, isReplyToBot) { TelegramId = telegramId };

    public void Dispose()
    {
        Reminders.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
