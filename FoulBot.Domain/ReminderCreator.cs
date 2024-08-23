namespace FoulBot.Domain;

public sealed record Reminder(
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    string RequestedByName);

// TODO: However, consider using linked CTS to make this implementation
// Cancel on Dispose to make it Bot-agnostic.
/// <summary>
/// This implementation is tightly coupled with the Bot class. There is no need
/// to cancel cancellationToken in Dispose method, because the cancellation
/// token that is passed into the constructor is being cancelled when the bot is
/// disposed.
/// </summary>
public sealed class ReminderCreator : IBotCommandProcessor, IAsyncDisposable
{
    private readonly ILogger<ReminderCreator> _logger;
    private readonly FoulChatId _chatId;
    private readonly FoulBotId _botId;
    private readonly FoulBot _bot;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _internalCts = new();
    private readonly CancellationTokenSource _cts;
    private readonly List<Reminder> _reminders;
    private Task? _running;

    public ReminderCreator(
        ILogger<ReminderCreator> logger,
        FoulChatId chatId,
        FoulBotId botId,
        FoulBot bot,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _chatId = chatId;
        _botId = botId;
        _bot = bot;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(
            _internalCts.Token, cancellationToken);

        if (!Directory.Exists("reminders"))
            Directory.CreateDirectory("reminders");

        // HACK: Sync read.
        if (!File.Exists($"reminders/{chatId}---{_botId.BotId}"))
            File.WriteAllText($"reminders/{chatId}---{_botId.BotId}", "[]");

        // HACK: Sync read.
        var content = File.ReadAllText($"reminders/{_chatId}---{_botId.BotId}");
        _reminders = JsonSerializer.Deserialize<List<Reminder>>(content)
            ?? throw new InvalidOperationException("Could not deserialize reminders.");

        if (_reminders.Count > 0)
            _running = RunRemindersAsync();
    }

    public ValueTask<bool> ProcessCommandAsync(FoulMessage message)
    {
        return AddReminderAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        await _internalCts.CancelAsync();

        _lock.Dispose();
        _cts.Dispose();
        _internalCts.Dispose();
    }

    private async ValueTask<bool> AddReminderAsync(FoulMessage foulMessage)
    {
        // TODO: Support english language and overall do better parsing.
        // This is legacy untouched code.
        if (foulMessage.Text.StartsWith($"@{_botId.BotId}"))
        {
            try
            {
                var message = foulMessage.Text.Replace($"@{_botId.BotId}", string.Empty).Trim();

                var requestedByName = foulMessage.SenderName;

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

                await AddReminderAsync(new Reminder(time, request, everyDay, requestedByName));

                return true;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not create a reminder.");
                return false;
            }
        }
        else
        {
            return false;
        }
    }

    private async ValueTask AddReminderAsync(Reminder reminder)
    {
        await _lock.WaitAsync(_cts.Token);
        try
        {
            _reminders.Add(reminder);
            await SaveRemindersAsync();

            _running ??= RunRemindersAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunRemindersAsync()
    {
        var captureToNotDispose = this;
        _ = captureToNotDispose;

        using var _l = _logger
            .AddScoped("ChatId", _chatId)
            .AddScoped("BotId", _botId.BotId)
            .BeginScope();

        while (_reminders.Count > 0) // As soon as we remove all reminders - this process stops.
        {
            await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);

            try
            {
                var dueReminders = _reminders
                    .Where(x => x.AtUtc <= DateTime.UtcNow)
                    .ToList(); // Make a copy so we can iterate and remove items if needed.

                foreach (var reminder in dueReminders)
                {
                    await _lock.WaitAsync(_cts.Token);
                    try
                    {
                        _logger.LogInformation("Reminding: {Reminder}", reminder);

                        _reminders.Remove(reminder);
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

                            _reminders.Add(newReminder);
                        }

                        await SaveRemindersAsync();
                    }
                    finally
                    {
                        _lock.Release();
                    }

                    await _bot.PerformRequestAsync(new(reminder.RequestedByName), reminder.Request);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not trigger a reminder.");
                // Do NOT rethrow exception here, as this will fail the whole reminders background task.
            }
        }

        _running = null; // After we removed all reminders - clear the process so it can start again.
    }

    private async ValueTask SaveRemindersAsync()
    {
        try
        {
            await File.WriteAllTextAsync(
                $"reminders/{_chatId}---{_botId.BotId}",
                JsonSerializer.Serialize(_reminders),
                CancellationToken.None /* Finish writing operation even when Host is stopping. */);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save reminders.");
            throw;
        }
    }
}
