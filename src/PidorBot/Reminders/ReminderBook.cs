using System.Globalization;
using PidorBot.Chats;

namespace PidorBot.Reminders;

public sealed record Reminder(
    string Id,
    ChatRef Chat,
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    string RequestedBy);

/// <summary>
/// Reminders of all chats, in memory. Saved to the same files as in the old FoulBot:
/// reminders/{chatId}---{botUsername}, a JSON array per chat.
/// </summary>
public sealed class ReminderBook : IDisposable
{
    private const string Separator = "---";

    private readonly ILogger<ReminderBook> _logger;
    private readonly string _directory;
    private readonly string _botUsername;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<ChatRef, List<Reminder>>? _reminders;

    public ReminderBook(ILogger<ReminderBook> logger, string directory, string botUsername)
    {
        _logger = logger;
        _directory = directory;
        _botUsername = botUsername;
    }

    public Task<IReadOnlyList<Reminder>> GetAsync(ChatRef chat)
        => LockedAsync<IReadOnlyList<Reminder>>(reminders
            => reminders.TryGetValue(chat, out var list) ? [.. list] : []);

    public async Task<Reminder> AddAsync(ChatRef chat, DateTime atUtc, string request, bool everyDay, string requestedBy)
    {
        var reminder = new Reminder(Guid.NewGuid().ToString(), chat, atUtc, request, everyDay, requestedBy);

        await LockedAsync(async reminders =>
        {
            Get(reminders, chat).Add(reminder);
            await SaveAsync(reminders, chat);
        });

        _logger.LogInformation("Added a reminder {@Reminder}", reminder);
        return reminder;
    }

    public Task<bool> RemoveAsync(ChatRef chat, string id)
        => LockedAsync(async reminders =>
        {
            if (Get(reminders, chat).RemoveAll(reminder => reminder.Id == id) == 0)
                return false;

            await SaveAsync(reminders, chat);
            return true;
        });

    /// <summary>
    /// Removes due reminders and returns them. Every-day reminders are moved to the next day.
    /// </summary>
    public Task<IReadOnlyList<Reminder>> TakeDueAsync(DateTime now)
        => LockedAsync<IReadOnlyList<Reminder>>(async reminders =>
        {
            var due = reminders.Values.SelectMany(list => list).Where(reminder => reminder.AtUtc <= now).ToList();

            foreach (var reminder in due)
            {
                var list = Get(reminders, reminder.Chat);
                list.Remove(reminder);

                if (reminder.EveryDay)
                {
                    var next = reminder;
                    while (next.AtUtc <= now)
                        next = next with { AtUtc = next.AtUtc.AddDays(1) };

                    list.Add(next);
                }
            }

            foreach (var chat in due.Select(reminder => reminder.Chat).Distinct())
                await SaveAsync(reminders, chat);

            return due;
        });

    public void Dispose() => _lock.Dispose();

    private static List<Reminder> Get(Dictionary<ChatRef, List<Reminder>> reminders, ChatRef chat)
    {
        if (!reminders.TryGetValue(chat, out var list))
            reminders[chat] = list = [];

        return list;
    }

    private Task LockedAsync(Func<Dictionary<ChatRef, List<Reminder>>, Task> action)
        => LockedAsync(async reminders =>
        {
            await action(reminders);
            return true;
        });

    private Task<T> LockedAsync<T>(Func<Dictionary<ChatRef, List<Reminder>>, T> action)
        => LockedAsync(reminders => Task.FromResult(action(reminders)));

    private async Task<T> LockedAsync<T>(Func<Dictionary<ChatRef, List<Reminder>>, Task<T>> action)
    {
        await _lock.WaitAsync();
        try
        {
            _reminders ??= await LoadAsync();
            return await action(_reminders);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<ChatRef, List<Reminder>>> LoadAsync()
    {
        Directory.CreateDirectory(_directory);

        var reminders = new Dictionary<ChatRef, List<Reminder>>();
        foreach (var path in Directory.EnumerateFiles(_directory, $"*{Separator}{_botUsername}"))
        {
            var chatId = Path.GetFileName(path).Split(Separator)[0];
            if (!long.TryParse(chatId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
                continue;

            try
            {
                var chat = new ChatRef(id);
                var saved = JsonSerializer.Deserialize<List<SavedReminder>>(await File.ReadAllTextAsync(path)) ?? [];
                reminders[chat] = [.. saved.Select(reminder => reminder.ToReminder(chat))];
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not load reminders from {Path}", path);
            }
        }

        _logger.LogInformation("Loaded {Count} reminders", reminders.Values.Sum(list => list.Count));
        return reminders;
    }

    private async Task SaveAsync(Dictionary<ChatRef, List<Reminder>> reminders, ChatRef chat)
    {
        var saved = Get(reminders, chat).Select(reminder => SavedReminder.Create(reminder, _botUsername));

        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{chat}{Separator}{_botUsername}"),
            JsonSerializer.Serialize(saved));
    }

    /// <summary>
    /// Format of the old FoulBot FileSavedReminder.
    /// </summary>
    private sealed record SavedReminder(
        string? Id, string ChatId, string BotId, DateTime AtUtc, string Request, bool EveryDay, string? From)
    {
        public static SavedReminder Create(Reminder reminder, string botUsername) => new(
            reminder.Id, reminder.Chat.ToString(), botUsername,
            reminder.AtUtc, reminder.Request, reminder.EveryDay, reminder.RequestedBy);

        public Reminder ToReminder(ChatRef chat) => new(
            Id ?? Guid.NewGuid().ToString(), chat,
            DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc), Request, EveryDay, From ?? "_");
    }
}
