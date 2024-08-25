namespace FoulBot.Domain.Tests;

public class ReminderCommandProcessorTests : Testing<ReminderFeature>
{
    private const string BotIdValue = "bot";
    private readonly Mock<IReminderStore> _reminderStore;
    private readonly Mock<IFoulBot> _bot;
    private readonly FoulChatId _chatId;
    private readonly FoulBotId _botId;
    private readonly FoulBotConfiguration _config;

    public ReminderCommandProcessorTests()
    {
        _reminderStore = Freeze<IReminderStore>();
        _bot = Freeze<IFoulBot>();
        _chatId = Fixture.Create<FoulChatId>();
        _botId = Fixture.Build<FoulBotId>()
            .With(x => x.BotId, BotIdValue)
            .Create();
        Fixture.Register(() => _chatId);

        _config = Fixture.Build<FoulBotConfiguration>()
            .With(x => x.FoulBotId, _botId)
            .Create();

        Customize("config", _config);
    }

    private void SetupReminders(IEnumerable<Reminder> reminders)
    {
        _reminderStore.Setup(x => x.GetRemindersForAsync(_chatId, _botId))
            .Returns(() => new(reminders));
    }

    [Theory, AutoMoqData]
    public async Task ShouldNotifyAboutReminderOnce(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(1))
                .With(x => x.EveryDay, false)
                .With(x => x.RequestedBy, requestedBy)
                .With(x => x.Request, request)
                .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();
        await WaitAsync();
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Once);

        var timeLeftTillDue = reminders[0].AtUtc - TimeProvider.GetUtcNow().UtcDateTime;
        TimeProvider.Advance(timeLeftTillDue - TimeSpan.FromSeconds(1));
        await WaitAsync();

        _bot.Verify(x => x.PerformRequestAsync(It.IsAny<ChatParticipant>(), It.IsAny<string>()), Times.Never);
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(2));

        TimeProvider.Advance(TimeSpan.FromSeconds(1) + ReminderFeature.CheckInterval);
        await WaitAsync();

        _bot.Verify(x => x.PerformRequestAsync(It.IsAny<ChatParticipant>(), It.IsAny<string>()));
        _reminderStore.Verify(x => x.RemoveReminderAsync(reminders[0]));
        _reminderStore.Verify(x => x.AddReminderAsync(It.IsAny<Reminder>()), Times.Never);
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(3));
    }

    [Theory, AutoMoqData]
    public async Task ShouldNotifyAboutDailyReminder_AndReAddItForTomorrow(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
            .Build<Reminder>()
            .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(1))
            .With(x => x.EveryDay, true)
            .With(x => x.RequestedBy, requestedBy)
            .With(x => x.Request, request)
            .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();
        await WaitAsync();
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Once);

        var timeLeftTillDue = reminders[0].AtUtc - TimeProvider.GetUtcNow().UtcDateTime;
        TimeProvider.Advance(timeLeftTillDue - TimeSpan.FromSeconds(1));
        await WaitAsync();

        _bot.Verify(x => x.PerformRequestAsync(It.IsAny<ChatParticipant>(), It.IsAny<string>()), Times.Never);
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(2));

        TimeProvider.Advance(TimeSpan.FromSeconds(1) + ReminderFeature.CheckInterval);
        await WaitAsync();

        _bot.Verify(x => x.PerformRequestAsync(It.IsAny<ChatParticipant>(), It.IsAny<string>()));
        _reminderStore.Verify(x => x.RemoveReminderAsync(reminders[0]));

        var newReminder = reminders[0] with { AtUtc = reminders[0].AtUtc + TimeSpan.FromDays(1) };
        _reminderStore.Verify(x => x.AddReminderAsync(newReminder));
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(3));
    }

    [Theory, AutoMoqData]
    public async Task ShouldNotifyAboutDailyReminder_AndReAddItForTomorrow_EvenIfDueTimeWas10DaysAgo(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
            .Build<Reminder>()
            .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromDays(10))
            .With(x => x.EveryDay, true)
            .With(x => x.RequestedBy, requestedBy)
            .With(x => x.Request, request)
            .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();
        await WaitAsync();

        TimeProvider.Advance(ReminderFeature.CheckInterval);
        await WaitAsync();

        _bot.Verify(x => x.PerformRequestAsync(It.IsAny<ChatParticipant>(), It.IsAny<string>()));
        _reminderStore.Verify(x => x.RemoveReminderAsync(reminders[0]));

        var newReminder = reminders[0] with { AtUtc = reminders[0].AtUtc + TimeSpan.FromDays(11) };
        _reminderStore.Verify(x => x.AddReminderAsync(newReminder));
    }

    [Theory, AutoMoqData]
    public async Task ShouldStopBackgroundProcess_WhenNoMoreReminders(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(1))
                .With(x => x.EveryDay, false)
                .With(x => x.RequestedBy, requestedBy)
                .With(x => x.Request, request)
                .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();
        await WaitAsync();
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Once);

        await AssertProcessStopsWhenNoReminders();
    }

    [Theory, AutoMoqData]
    public async Task ShouldStopBackgroundProcess_OnDispose(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(1))
                .With(x => x.EveryDay, false)
                .With(x => x.RequestedBy, requestedBy)
                .With(x => x.Request, request)
                .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        {
            await using var sut = Fixture.Create<ReminderFeature>();
            await WaitAsync();
            _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Once);
        } // Disposing of sut.

        await AssertProcessStops(1);
    }

    [Theory, AutoMoqData]
    public async Task ShouldStopBackgroundProcess_OnStop(
        ChatParticipant requestedBy,
        string request)
    {
        var reminders = new List<Reminder>
        {
            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(1))
                .With(x => x.EveryDay, false)
                .With(x => x.RequestedBy, requestedBy)
                .With(x => x.Request, request)
                .Create(),

            Fixture
                .Build<Reminder>()
                .With(x => x.AtUtc, TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromHours(100))
                .Create()
        };

        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();
        await WaitAsync();
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Once);

        await sut.StopFeatureAsync();
        await AssertProcessStops(1);
    }

    [Theory]
    [InlineAutoMoqData($"@{BotIdValue} через 15 минут request", false, 15 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} через 15 м request", false, 15 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} каждый день через 15 минут request", true, 15 * 60)]
    [InlineAutoMoqData($"   @{BotIdValue}   каждый день     через   15      минут   request    ", true, 15 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} через 120 секунд request", false, 2 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} через 120 сек request", false, 2 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} через 120 с request", false, 2 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} in 15 minutes request", false, 15 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} every day in 15 minutes request", true, 15 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} after 3 hours request", false, 3 * 60 * 60)]
    [InlineAutoMoqData($"@{BotIdValue} через 3 д request", false, 3 * 24 * 60 * 60)]
    public async Task ShouldProcessAddReminderCommand(
        string messageText, bool isEveryDay, int advanceSeconds)
    {
        var request = "request";

        await using var sut = Fixture.Create<ReminderFeature>();

        var message = Fixture
            .Build<FoulMessage>()
            .With(x => x.Text, messageText)
            .Create();

        await sut.ProcessMessageAsync(message);

        _reminderStore.Verify(x => x.AddReminderAsync(It.Is<Reminder>(
            r => r.Request == request
            && r.RequestedBy == message.Sender
            && r.BotId == _botId
            && r.ChatId == _chatId
            && r.AtUtc == TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(advanceSeconds)
            && r.EveryDay == isEveryDay)));
    }

    [Theory]
    [InlineAutoMoqData($"@{BotIdValue} через 15 м request", 15 * 60)]
    public async Task ShouldProcessAddReminderCommand_AndRestartProcessingIfNeede(
        string messageText, int advanceSeconds)
    {
        var request = "request";

        SetupReminders([]);
        await using var sut = Fixture.Create<ReminderFeature>();
        await AssertProcessStopsWhenNoReminders(1);

        var message = Fixture
            .Build<FoulMessage>()
            .With(x => x.Text, messageText)
            .Create();

        SetupReminders([]); // Processing exits straightaway.

        await sut.ProcessMessageAsync(message);

        _reminderStore.Verify(x => x.AddReminderAsync(It.Is<Reminder>(
            r => r.Request == request
            && r.RequestedBy == message.Sender
            && r.BotId == _botId
            && r.ChatId == _chatId
            && r.AtUtc == TimeProvider.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(advanceSeconds)
            && r.EveryDay == false)));

        // Processing started working again.
        await WaitAsync();
        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(2));
    }

    [Theory]
    [InlineAutoMoqData($"@{BotIdValue} отмени напоминание 1234", "1234")]
    [InlineAutoMoqData($"@{BotIdValue} cancel reminder abc", "abc")]
    public async Task ShouldProcessCancelReminderCommand(
        string command, string reminderId)
    {
        var reminder = Fixture.Build<Reminder>()
            .With(x => x.Id, reminderId)
            .Create();

        SetupReminders([reminder]);

        await using var sut = Fixture.Create<ReminderFeature>();

        await sut.ProcessMessageAsync(Fixture.Create<FoulMessage>() with
        {
            Text = command
        });

        _reminderStore.Verify(x => x.RemoveReminderAsync(reminder));
        _bot.Verify(x => x.SendRawAsync(It.Is<string>(t => t.Contains("was removed"))));
    }

    [Theory]
    [InlineAutoMoqData($"@{BotIdValue} show reminders")]
    [InlineAutoMoqData($"@{BotIdValue} покажи напоминания")]
    public async Task ShouldProcessShowRemindersCommand_EscapingMarkdownCharacters(
        string command, IList<Reminder> reminders)
    {
        reminders[0] = reminders[0] with
        {
            Request = reminders[0].Request + "_some -signs (for )escaping"
        };
        SetupReminders(reminders);

        await using var sut = Fixture.Create<ReminderFeature>();

        await sut.ProcessMessageAsync(Fixture.Create<FoulMessage>() with
        {
            Text = command
        });

        foreach (var reminder in reminders)
        {
            // TODO: Verify escaping reminder.Request too. Or maybe later, when I move this to Telegram.
            _bot.Verify(x => x.SendRawAsync(It.Is<string>(
                t => t.Contains(reminder.Id.Replace("-", @"\-")))));
        }
    }

    private async Task AssertProcessStopsWhenNoReminders(int times = 2)
    {
        SetupReminders([]);

        await AssertProcessStops(times);
    }

    private async Task AssertProcessStops(int times)
    {
        TimeProvider.Advance(ReminderFeature.CheckInterval);
        await WaitAsync();

        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(times));

        // No more calls even after we set up reminders again.
        SetupReminders(Fixture.Create<IEnumerable<Reminder>>());
        TimeProvider.Advance(TimeSpan.FromHours(100) + ReminderFeature.CheckInterval);
        await WaitAsync();

        _reminderStore.Verify(x => x.GetRemindersForAsync(_chatId, _botId), Times.Exactly(times));
    }
}
