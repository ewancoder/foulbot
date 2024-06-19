using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FoulBot.Domain;

public sealed record Reminder(
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    string From)
{
    public string? Id { get; set; }
}

public sealed class ReminderCreator
{
    private readonly FoulChatId _chatId;

    private readonly string _botId;
    private readonly object _lock = new object();
    private readonly Dictionary<string, Reminder> _reminders;

    public ReminderCreator(
        FoulChatId chatId,
        string botId)
    {
        _chatId = chatId;
        _botId = botId;

        if (!Directory.Exists("reminders"))
            Directory.CreateDirectory("reminders");

        if (!File.Exists($"reminders/{chatId}---{_botId}"))
            File.WriteAllText($"reminders/{chatId}---{botId}", "[]");

        var content = File.ReadAllText($"reminders/{_chatId}---{_botId}");
        _reminders = JsonSerializer.Deserialize<IEnumerable<Reminder>>(content)!
            .ToDictionary(x => x.Id!);

        var copyOfReminders = _reminders.Values.ToList();

        _ = Task.WhenAll(copyOfReminders.Select(
            reminder => InitializeReminderAsync(reminder.Id!)).ToList());
    }

    public IEnumerable<Reminder> AllReminders => _reminders.Values;
    public EventHandler<Reminder>? Remind;

    public void AddReminder(Reminder reminder)
    {
        var id = Guid.NewGuid().ToString();

        lock (_lock)
        {
            reminder.Id = id;
            _reminders.Add(id, reminder);
            SaveReminders();
        }

        _ = InitializeReminderAsync(id);
    }

    public void AddReminder(FoulMessage foulMessage)
    {
        var message = foulMessage.Text.Replace($"@{_botId}", string.Empty).Trim();
        var from = foulMessage.SenderName;

        var everyDay = false;
        if (message.StartsWith("каждый день"))
        {
            everyDay = true;
            message = message.Replace("каждый день", string.Empty);
        }

        var command = message.Replace("через", string.Empty).Trim();
        var number = Convert.ToInt32(command.Split(' ')[0]);
        var units = command.Split(' ')[1];
        var request = string.Join(' ', command.Split(' ').Skip(2));

        var time = DateTime.UtcNow;
        if (units.StartsWith("сек"))
            time = DateTime.UtcNow + TimeSpan.FromSeconds(number);
        if (units.StartsWith("мин"))
            time = DateTime.UtcNow + TimeSpan.FromMinutes(number);
        if (units.StartsWith("час"))
            time = DateTime.UtcNow + TimeSpan.FromHours(number);
        if (units.StartsWith("де") || units.StartsWith("дн"))
            time = DateTime.UtcNow + TimeSpan.FromDays(number);

        AddReminder(new Reminder(time, request, everyDay, from));
    }

    private async Task InitializeReminderAsync(string id)
    {
        await Task.Yield();

        var reminder = _reminders[id];
        if (reminder.AtUtc + TimeSpan.FromSeconds(1) > DateTime.UtcNow)
            await Task.Delay(reminder.AtUtc + TimeSpan.FromSeconds(2) - DateTime.UtcNow);

        lock (_lock)
        {
            _reminders.Remove(id);
            if (reminder.EveryDay)
            {
                _reminders.Add(id, reminder with
                {
                    AtUtc = reminder.AtUtc + TimeSpan.FromDays(1)
                });
            }
            SaveReminders();
        }

        TriggerReminder(reminder);
    }

    private void TriggerReminder(Reminder reminder)
    {
        Remind?.Invoke(this, reminder);
    }

    private void SaveReminders()
    {
        try
        {
            File.WriteAllText($"reminders/{_chatId}---{_botId}", JsonSerializer.Serialize(_reminders.Values));
        }
        catch
        {
            // TODO: Log this.
        }
    }
}
