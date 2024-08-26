namespace FoulBot.Domain.Storage;

/// <summary>
/// Should be in-memory cached, it is queryed very often.
/// </summary>
public interface IReminderStore
{
    ValueTask<IEnumerable<Reminder>> GetRemindersForAsync(
        FoulChatId chatId, FoulBotId botId);

    ValueTask AddReminderAsync(Reminder reminder);
    ValueTask RemoveReminderAsync(Reminder reminder);
}
