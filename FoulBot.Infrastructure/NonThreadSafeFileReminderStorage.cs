using FoulBot.Domain.Features;
using FoulBot.Domain.Storage;

namespace FoulBot.Infrastructure;

public sealed record FileSavedReminder(
    string Id,
    string ChatId,
    string BotId,
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    string From)
{
    public static FileSavedReminder FromReminder(Reminder reminder) => new(
        reminder.Id,
        reminder.ChatId.Value,
        reminder.BotId.BotId,
        reminder.AtUtc,
        reminder.Request,
        reminder.EveryDay,
        reminder.RequestedBy.Name);

    public static Reminder ToReminder(FileSavedReminder dto, FoulChatId chatId, FoulBotId botId) => new(
        dto.Id ?? Guid.NewGuid().ToString(),
        chatId,
        botId,
        dto.AtUtc,
        dto.Request,
        dto.EveryDay,
        dto.From == null ? new("_") : new(dto.From));
}

/// <summary>
/// Is not thread-safe, any usage should be wrapped in lock.
/// </summary>
public sealed class NonThreadSafeFileReminderStorage : IReminderStore
{
    public async ValueTask<IEnumerable<Reminder>> GetRemindersForAsync(FoulChatId chatId, FoulBotId botId)
    {
        if (!Directory.Exists("reminders"))
            Directory.CreateDirectory("reminders");

        if (!File.Exists($"reminders/{chatId.Value}---{botId.BotId}"))
            await File.WriteAllTextAsync($"reminders/{chatId.Value}---{botId.BotId}", "[]");

        var content = await File.ReadAllTextAsync($"reminders/{chatId.Value}---{botId.BotId}");
        var reminders = JsonSerializer.Deserialize<IEnumerable<FileSavedReminder>>(content)
            ?? throw new InvalidOperationException("Could not deserialize reminders.");

        return reminders.Select(x => FileSavedReminder.ToReminder(x, chatId, botId)).ToList();
    }

    public async ValueTask AddReminderAsync(Reminder reminder)
    {
        var all = (await GetRemindersForAsync(reminder.ChatId, reminder.BotId))
            .ToList();

        all.Add(reminder);

        await SaveAsync(all, reminder.ChatId, reminder.BotId);
    }

    public async ValueTask RemoveReminderAsync(Reminder reminder)
    {
        var all = (await GetRemindersForAsync(reminder.ChatId, reminder.BotId))
            .ToList();

        all.RemoveAt(all.FindIndex(x => x.Id == reminder.Id));

        await SaveAsync(all, reminder.ChatId, reminder.BotId);
    }

    private static async ValueTask SaveAsync(
        IEnumerable<Reminder> reminders, FoulChatId chatId, FoulBotId botId)
    {
        var serialized = JsonSerializer.Serialize(reminders.Select(FileSavedReminder.FromReminder));
        await File.WriteAllTextAsync($"reminders/{chatId.Value}---{botId.BotId}", serialized);
    }
}
