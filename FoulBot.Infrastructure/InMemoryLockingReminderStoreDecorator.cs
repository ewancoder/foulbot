using System.Collections.Concurrent;
using FoulBot.Domain.Storage;

namespace FoulBot.Infrastructure;

// TODO: Consider moving to infrastructure (or helper project). It's not FoulBot domain.
public sealed class InMemoryLockingReminderStoreDecorator : IReminderStore, IDisposable
{
    private readonly IReminderStore _store;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, List<Reminder>> _reminders = [];

    public InMemoryLockingReminderStoreDecorator(IReminderStore store)
    {
        _store = store;
    }

    public async ValueTask AddReminderAsync(Reminder reminder)
    {
        var list = _reminders.GetOrAdd(GetKey(reminder), []);

        await _lock.WaitAsync();
        try
        {
            list.Add(reminder);
            await _store.AddReminderAsync(reminder);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask<IEnumerable<Reminder>> GetRemindersForAsync(FoulChatId chatId, FoulBotId botId)
    {
        if (_reminders.TryGetValue(GetKey(chatId, botId), out var list))
            return list;

        await _lock.WaitAsync();
        try
        {
            var reminders = await _store.GetRemindersForAsync(chatId, botId);
            _reminders.GetOrAdd(GetKey(chatId, botId), [.. reminders]);
            return reminders;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask RemoveReminderAsync(Reminder reminder)
    {
        if (_reminders.TryGetValue(GetKey(reminder), out var list))
        {
            await _lock.WaitAsync();
            try
            {
                var index = list.FindIndex(x => x.Id == reminder.Id);
                if (index >= 0)
                    list.RemoveAt(index);

                await _store.RemoveReminderAsync(reminder);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    private static string GetKey(FoulChatId chatId, FoulBotId botId)
        => $"{chatId.Value}${botId.BotId}";

    private static string GetKey(Reminder reminder)
        => GetKey(reminder.ChatId, reminder.BotId);
}
