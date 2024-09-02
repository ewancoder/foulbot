using System.Text;
using FoulBot.Domain.Storage;

namespace FoulBot.Domain.Features;

public sealed record Reminder(
    string Id, // Unfortunately we need an identifier in our domain model, so we can properly remove reminders.
    FoulChatId ChatId,
    FoulBotId BotId,
    DateTime AtUtc,
    string Request,
    bool EveryDay,
    ChatParticipant RequestedBy);

public sealed class ReminderFeature : BotFeature, IAsyncDisposable
{
    // TODO: Consider moving out "markdown" logic to telegram-specific dependencies.
    public const string EscapedCharacters = "-_()";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
    private readonly ILogger<ReminderFeature> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IReminderStore _reminderStore;
    private readonly FoulBotConfiguration _config;
    private readonly FoulChatId _chatId;
    private readonly IFoulBot _bot;
    private readonly CancellationTokenSource _localCts = new();
    private readonly CancellationTokenSource _cts;
    private readonly object _lock = new();
    private Task? _processing;
    private bool _isStopping;

    public ReminderFeature(
        ILogger<ReminderFeature> logger,
        TimeProvider timeProvider,
        IReminderStore reminderStore,
        FoulBotConfiguration config,
        FoulChatId chatId,
        IFoulBot bot,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _reminderStore = reminderStore;
        _config = config;
        _chatId = chatId;
        _bot = bot;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(
            _localCts.Token, cancellationToken);

        _processing = ProcessRemindersAsync();

        // Make sure if the task is synchronously completed - reset the task.
        if (_processing.IsCompleted)
            _processing = null;
    }

    public override async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
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
                _logger.LogDebug("Processing cancel reminder command.");

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
                _logger.LogDebug("Processing show reminders command.");

                var reminders = await _reminderStore.GetRemindersForAsync(_chatId, _config.FoulBotId);

                var sb = new StringBuilder();
                sb.AppendLine(@"*Reminders*");
                foreach (var rem in reminders)
                {
                    sb.AppendLine();
                    sb.AppendLine($"`{rem.Id}` - {rem.RequestedBy.Name} - {rem.AtUtc} {(rem.EveryDay ? $"- EVERY DAY " : string.Empty)}- *{rem.Request}*");
                }

                foreach (var esc in EscapedCharacters)
                {
                    sb = sb
                        .Replace($"{esc}", $@"\{esc}");
                }

                var escapedMarkdown = sb.ToString();

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

            _logger.LogDebug("Processing add new reminder command.");

            var requestedBy = message.Sender;

            text = text.Trim();
            var parts = text.Split(' ').Where(x => x.Length > 0).ToList();
            var number = Convert.ToInt32(parts[0]);
            var units = parts[1];
            var request = string.Join(' ', parts.Skip(2));

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            var time = now;
            if (units.StartsWith('с') || units.StartsWith('s'))
                time = now + TimeSpan.FromSeconds(number);
            if (units.StartsWith('м') || units.StartsWith('m'))
                time = now + TimeSpan.FromMinutes(number);
            if (units.StartsWith('ч') || units.StartsWith('h'))
                time = now + TimeSpan.FromHours(number);
            if (units.StartsWith('д') || units.StartsWith('d'))
                time = now + TimeSpan.FromDays(number);

            var reminder = new Reminder(
                Guid.NewGuid().ToString(),
                _chatId, _config.FoulBotId,
                time, request, isEveryDay, requestedBy);
            await _reminderStore.AddReminderAsync(reminder);
            _logger.LogInformation("Successfully added a new reminder: {@Reminder}", reminder);

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

    public override async ValueTask StopFeatureAsync()
    {
        _isStopping = true;

        await _localCts.CancelAsync();
        await DisposeAsync(); // HACK: Dispose will be called twice when disposed.
    }

    private async Task ProcessRemindersAsync()
    {
        _logger.LogDebug("Starting processing reminders mechanism");

        while (true)
        {
            // This is outside of try/catch because it needs to fail on cancellation.
            await Task.Delay(CheckInterval, _timeProvider, _cts.Token);

            try // Unit test not failing this process on exceptions.
            {
                // This call should be cached in-memory.
                var reminders = await _reminderStore.GetRemindersForAsync(_chatId, new(_config.BotId, _config.BotName));
                if (!reminders.Any())
                {
                    _logger.LogDebug("No reminders found for this bot in this chat. Exiting processing reminders");
                    _processing = null;
                    break; // As soon as we remove all reminders - this process stops.
                }

                var dueReminders = reminders
                    .Where(x => x.AtUtc <= _timeProvider.GetUtcNow().UtcDateTime)
                    .ToList(); // We need to copy it because _reminderStore is cached in memory, and underlying collection will be modified.

                foreach (var reminder in dueReminders)
                {
                    try
                    {
                        _logger.LogInformation("Reminding: {@Reminder}", reminder);

                        await _reminderStore.RemoveReminderAsync(reminder);
                        if (reminder.EveryDay)
                        {
                            var newReminder = reminder;
                            while (newReminder.AtUtc <= _timeProvider.GetUtcNow().UtcDateTime)
                            {
                                newReminder = newReminder with
                                {
                                    AtUtc = newReminder.AtUtc + TimeSpan.FromDays(1)
                                };
                            }

                            _logger.LogDebug("It's an every-day reminder. Resetting it for the next day");
                            await _reminderStore.AddReminderAsync(newReminder);
                        }

                        await _bot.PerformRequestAsync(reminder.RequestedBy, reminder.Request);
                    }
                    catch (Exception exception)
                    {
                        // TODO: Unit test this. So far untested.
                        _logger.LogError(exception, "Could not trigger a reminder");
                        // Do NOT rethrow exception here, as this will fail the whole reminders background task.
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not process a reminder");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isStopping)
            await StopFeatureAsync();

        _localCts.Dispose();
        _cts.Dispose();
    }
}
