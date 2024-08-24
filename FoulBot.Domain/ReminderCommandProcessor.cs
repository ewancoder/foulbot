using System.Text;

namespace FoulBot.Domain;

public sealed record Reminder(
    string Id, // Unfortunately we need an identifier in our domain model, so we can properly remove reminders.
    FoulChatId ChatId,
    FoulBotId BotId,
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    ChatParticipant RequestedBy);

public sealed class ReminderCommandProcessor : IBotCommandProcessor, IAsyncDisposable
{
    private readonly ILogger<ReminderCommandProcessor> _logger;
    private readonly IReminderStore _reminderStore;
    private readonly FoulBotConfiguration _config;
    private readonly FoulChatId _chatId;
    private readonly IFoulBot _bot;
    private readonly CancellationTokenSource _localCts = new();
    private readonly CancellationTokenSource _cts;
    private readonly object _lock = new();
    private Task? _processing;

    public ReminderCommandProcessor(
        ILogger<ReminderCommandProcessor> logger,
        IReminderStore reminderStore,
        FoulBotConfiguration config,
        FoulChatId chatId,
        IFoulBot bot,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _reminderStore = reminderStore;
        _config = config;
        _chatId = chatId;
        _bot = bot;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(
            _localCts.Token, cancellationToken);

        _processing = ProcessRemindersAsync();
    }

    public async ValueTask<bool> ProcessCommandAsync(FoulMessage message)
    {
        try
        {
            var text = CutKeyword(message.Text, $"@{_config.BotId}");
            if (text is null)
                return false;

            var reminderId = CutKeyword(text, "отмени напоминание")
                ?? CutKeyword(text, "cancel reminder");

            if (reminderId is not null)
            {
                var reminders = await _reminderStore.GetRemindersForAsync(_chatId, _config.FoulBotId);
                var reminderForRemoval = reminders.FirstOrDefault(x => x.Id == reminderId);
                if (reminderForRemoval != null)
                    await _reminderStore.RemoveReminderAsync(reminderForRemoval);

                await _bot.SendRawAsync($"Reminder {reminderId} was removed.");

                return true;
            }

            var remindersListCommand = CutKeyword(text, "покажи напоминания")
                ?? CutKeyword(text, "show reminders");

            if (remindersListCommand is not null)
            {
                var reminders = await _reminderStore.GetRemindersForAsync(_chatId, _config.FoulBotId);

                var sb = new StringBuilder();
                sb.AppendLine(@"*Reminders*");
                foreach (var rem in reminders)
                {
                    sb.AppendLine();
                    sb.AppendLine($"`{rem.Id}` - {rem.RequestedBy.Name} - {rem.AtUtc} {(rem.EveryDay ? $"- EVERY DAY " : string.Empty)}- *{rem.Request}*");
                }

                var escapedMarkdown = sb
                    .Replace("-", @"\-")
                    .Replace("_", @"\_")
                    .Replace("(", @"\(")
                    .Replace(")", @"\)")
                    .ToString();

                await _bot.SendRawAsync(escapedMarkdown);

                return true;
            }

            var everyDay = CutKeyword(text, "каждый день")
                ?? CutKeyword(text, "every day")
                ?? CutKeyword(text, "each day");

            var isEveryDay = everyDay != null;
            text = everyDay ?? text;

            text = CutKeyword(text, "через")
                ?? CutKeyword(text, "in")
                ?? CutKeyword(text, "after");

            if (text == null)
                return false;

            var requestedBy = message.Sender;

            var number = Convert.ToInt32(text.Split(' ')[0]);
            var units = text.Split(' ')[1];
            var request = string.Join(' ', text.Split(' ').Skip(2));

            var time = DateTime.UtcNow;
            if (units.StartsWith("сек"))
                time = DateTime.UtcNow + TimeSpan.FromSeconds(number);
            if (units.StartsWith("мин"))
                time = DateTime.UtcNow + TimeSpan.FromMinutes(number);
            if (units.StartsWith("час"))
                time = DateTime.UtcNow + TimeSpan.FromHours(number);
            if (units.StartsWith("де") || units.StartsWith("дн"))
                time = DateTime.UtcNow + TimeSpan.FromDays(number);

            var reminder = new Reminder(
                Guid.NewGuid().ToString(),
                _chatId, _config.FoulBotId,
                time, request, isEveryDay, requestedBy);
            await _reminderStore.AddReminderAsync(reminder);

            lock (_lock)
            {
                _processing ??= ProcessRemindersAsync();
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not process reminders command.");
            return false;
        }
    }

    public async ValueTask StopProcessingAsync()
    {
        await DisposeAsync();
    }

    private static string? CutKeyword(string text, string keyword)
    {
        if (!text.StartsWith(keyword))
            return null;

        return text[(0 + keyword.Length)..].Trim();
    }

    private async Task ProcessRemindersAsync()
    {
        while (true)
        {
            // This call should be cached in-memory.
            var reminders = await _reminderStore.GetRemindersForAsync(_chatId, new(_config.BotId, _config.BotName));
            if (!reminders.Any())
            {
                _processing = null;
                break; // As soon as we remove all reminders - this process stops.
            }

            await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);

            var dueReminders = reminders
                .Where(x => x.AtUtc <= DateTime.UtcNow);

            foreach (var reminder in dueReminders)
            {
                try
                {
                    _logger.LogInformation("Reminding: {Reminder}", reminder);

                    await _reminderStore.RemoveReminderAsync(reminder);
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

                        await _reminderStore.AddReminderAsync(newReminder);
                    }

                    await _bot.PerformRequestAsync(reminder.RequestedBy, reminder.Request);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Could not trigger a reminder.");
                    // Do NOT rethrow exception here, as this will fail the whole reminders background task.
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _localCts.CancelAsync();
        _localCts.Dispose();
        _cts.Dispose();
    }
}
