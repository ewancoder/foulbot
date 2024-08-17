namespace FoulBot.Domain;

public sealed record Reminder(
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    string From)
{
    public string? Id { get; set; }
}

public sealed record FoulBotId(string BotId, string BotName);

public sealed class ReminderCreator
{
    private readonly FoulChatId _chatId;
    private readonly FoulBotId _botId;

    private readonly object _lock = new();
    private readonly Dictionary<string, Reminder> _reminders;
    private readonly ILogger<ReminderCreator> _logger;
    private readonly Task _running;

    public ReminderCreator(
        ILogger<ReminderCreator> logger,
        FoulChatId chatId,
        FoulBotId botId,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _chatId = chatId;
        _botId = botId;

        if (!Directory.Exists("reminders"))
            Directory.CreateDirectory("reminders");

        if (!File.Exists($"reminders/{chatId}---{_botId.BotId}"))
            File.WriteAllText($"reminders/{chatId}---{_botId.BotId}", "[]");

        var content = File.ReadAllText($"reminders/{_chatId}---{_botId.BotId}");
        _reminders = JsonSerializer.Deserialize<IEnumerable<Reminder>>(content)!
            .ToDictionary(x => x.Id!);

        _running = RunRemindersAsync(cancellationToken);
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
    }

    public void AddReminder(FoulMessage foulMessage)
    {
        var message = foulMessage.Text.Replace($"@{_botId.BotId}", string.Empty).Trim();
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

    private async Task RunRemindersAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger
            .AddScoped("ChatId", _chatId)
            .AddScoped("BotId", _botId.BotId)
            .BeginScope();

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

            try
            {
                var dueReminders = _reminders.Values
                    .Where(x => x.AtUtc <= DateTime.UtcNow)
                    .ToList();

                foreach (var reminder in dueReminders)
                {
                    lock (_lock)
                    {
                        _logger.LogInformation("Reminding: {Reminder}", reminder);

                        _reminders.Remove(reminder.Id!);
                        if (reminder.EveryDay)
                        {
                            var newReminder = reminder;
                            while (newReminder.AtUtc <= DateTime.UtcNow)
                            {
                                newReminder = newReminder with
                                {
                                    AtUtc = newReminder.AtUtc + TimeSpan.FromDays(1)
                                };
                            }

                            _reminders.Add(newReminder.Id!, newReminder);
                        }
                        SaveReminders();
                    }

                    TriggerReminder(reminder);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not trigger a reminder.");
            }
        }
    }

    private void TriggerReminder(Reminder reminder)
    {
        Remind?.Invoke(this, reminder);
    }

    private void SaveReminders()
    {
        try
        {
            File.WriteAllText($"reminders/{_chatId}---{_botId.BotId}", JsonSerializer.Serialize(_reminders.Values));
        }
        catch
        {
            // TODO: Log this.
        }
    }

    public void CancelReminder(string reminderId)
    {
        lock (_lock)
        {
            _reminders.Remove(reminderId);
        }
    }
}
