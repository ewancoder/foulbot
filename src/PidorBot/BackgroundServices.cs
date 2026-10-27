using Microsoft.Extensions.Hosting;
using PidorBot.Chats;
using PidorBot.Reminders;

namespace PidorBot;

/// <summary>
/// Gives the bot time to think: talk on its own, update memories.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly ChatHub _hub;
    private readonly TimeProvider _time;

    public HeartbeatService(ILogger<HeartbeatService> logger, ChatHub hub, TimeProvider time)
    {
        _logger = logger;
        _hub = hub;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _hub.LoadGroupsAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load chats");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _hub.Tick();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Heartbeat failed");
            }
        }
    }
}

public sealed class ReminderService : BackgroundService
{
    private readonly ILogger<ReminderService> _logger;
    private readonly ReminderBook _reminders;
    private readonly ChatHub _hub;
    private readonly TimeProvider _time;

    public ReminderService(ILogger<ReminderService> logger, ReminderBook reminders, ChatHub hub, TimeProvider time)
    {
        _logger = logger;
        _reminders = reminders;
        _hub = hub;
        _time = time;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), _time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var reminder in await _reminders.TakeDueAsync(_time.GetUtcNow().UtcDateTime))
                    _ = RemindAsync(reminder);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Could not check reminders");
            }
        }
    }

    private async Task RemindAsync(Reminder reminder)
    {
        try
        {
            _logger.LogInformation("Reminding {@Reminder}", reminder);
            await _hub.RemindAsync(reminder);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not remind {@Reminder}", reminder);
        }
    }
}
