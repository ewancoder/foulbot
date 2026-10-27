using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PidorBot.AI;
using PidorBot.Brain;
using PidorBot.Chats;
using PidorBot.Reminders;
using PidorBot.Storage;
using PidorBot.Telegram;

namespace PidorBot.Tests;

public sealed class ReplyPolicyTests
{
    private readonly ReplyPolicy _policy = new("ew_pidorbot");
    private readonly DateTime _now = TestBot.Start.UtcDateTime;

    [Theory]
    [InlineData("бот")]
    [InlineData("Бот, привет")]
    [InlineData("ну ты и пидор!")]
    [InlineData("эй @ew_pidorbot")]
    [InlineData("hello bot")]
    public void ShouldTrigger_OnWholeWords(string text)
        => Assert.Equal(ReplyReason.Trigger, Decide(text, lastKeywordReplyAt: _now));

    [Theory]
    [InlineData("работа")]
    [InlineData("пидоры")]
    public void ShouldReplyToKeywords_AfterCooldown(string text)
    {
        Assert.Equal(ReplyReason.None, Decide(text, lastKeywordReplyAt: _now));
        Assert.Equal(ReplyReason.Keyword, Decide(text, lastKeywordReplyAt: _now - ReplyPolicy.KeywordCooldown));
    }

    [Fact]
    public void ShouldReply_ToRepliesToBot()
        => Assert.Equal(
            ReplyReason.ReplyToBot,
            _policy.Decide([TestBot.Message("ну", isReplyToBot: true)], isPrivateChat: false, _now, _now));

    [Fact]
    public void ShouldIgnore_OwnMessages()
        => Assert.Equal(
            ReplyReason.None,
            _policy.Decide([ChatMessage.FromBot("я бот", _now)], isPrivateChat: true, _now, DateTime.MinValue));

    private ReplyReason Decide(string text, DateTime lastKeywordReplyAt)
        => _policy.Decide([TestBot.Message(text)], isPrivateChat: false, _now, lastKeywordReplyAt);
}

public sealed class ResponderTests
{
    private readonly Responder _responder = new(NullLogger<Responder>.Instance, new FakeLlm(), new Random(1));

    [Fact]
    public void ShouldBuildPrompt_WithEverythingTheBotKnows()
    {
        var photo = BinaryData.FromBytes([1]);
        var message = TestBot.Message("смотри", 5) with { PhotoFileId = "photo" };
        var prompt = _responder.BuildPrompt(new ReplyRequest(
            ReplyKind.Reply, Modes.Mild, "sleepy.", "ЛЮДИ: Иван любит пиво", [message, ChatMessage.FromBot("ага", message.Date)],
            TestBot.Start, Photos: new Dictionary<string, BinaryData> { [message.Id] = photo }, Reminders: []));

        Assert.Equal(4, prompt.Count);
        Assert.Contains(Modes.Mild.Directive, prompt[0].Text);
        Assert.Contains("Иван любит пиво", prompt[0].Text);
        Assert.Equal("[msg 5] Ivan_Petrov: смотри", prompt[1].Text);
        Assert.Equal([photo], prompt[1].Images);
        Assert.Equal(new LlmMessage(LlmRole.Assistant, "ага").Text, prompt[2].Text);
        Assert.Contains("Your mood right now: sleepy.", prompt[3].Text);
        Assert.Contains("Style of your next reply", prompt[3].Text);
    }

    [Theory]
    [InlineData("Pidor_Bot: отстань", "отстань")]
    [InlineData("[msg 12] Ivan_Petrov: отстань", "отстань")]
    [InlineData("  отстань  ", "отстань")]
    public void ShouldClean_MimickedPrefixes(string reply, string expected)
        => Assert.Equal(expected, Responder.Clean(reply, [TestBot.Message("x")]));
}

public sealed class StorageCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pidorbot-tests-{Guid.NewGuid()}");

    public StorageCompatibilityTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ShouldRead_OldFoulBotHistory()
    {
        const string user = """{"Id":"1-2-3","Type":1,"SenderType":3,"Sender":"Ivan_Petrov","Text":"бот привет","Date":"2025-01-02T10:00:00Z","IsOriginallyBotMessage":false,"ReplyTo":"ew_pidorbot","SenderName":"Ivan_Petrov","ForceReply":false}""";
        const string pidor = """{"Id":"4","Type":1,"SenderType":2,"Sender":"Pidor_Bot","Text":"чего","Date":"2025-01-02T10:00:05Z","IsOriginallyBotMessage":true,"ReplyTo":null}""";
        const string otherBot = """{"Id":"5","Type":1,"SenderType":2,"Sender":"Vova","Text":"кофе","Date":"2025-01-02T10:00:06Z","IsOriginallyBotMessage":true,"ReplyTo":null}""";
        const string document = """{"Id":"6","Type":2,"SenderType":3,"Sender":"Ivan_Petrov","Text":"*sent document*","Date":"2025-01-02T10:00:07Z","IsOriginallyBotMessage":false}""";

        var messages = new[] { user, pidor, otherBot, document, "garbage" }
            .Select(StoredMessage.Deserialize)
            .OfType<StoredMessage>()
            .Select(message => message.ToChatMessage(TestBot.Identity))
            .ToList();

        Assert.Equal(3, messages.Count);
        Assert.True(messages[0].IsReplyToBot);
        Assert.False(messages[0].IsFromBot);
        Assert.Equal(DateTimeKind.Utc, messages[0].Date.Kind);
        Assert.True(messages[1].IsFromBot);
        Assert.False(messages[2].IsFromBot);
    }

    [Fact]
    public void ShouldRoundTrip_NewHistory()
    {
        var message = TestBot.Message("привет", 7, isReplyToBot: true) with { PhotoFileId = "photo" };

        var restored = StoredMessage.Deserialize(StoredMessage.Serialize(message, TestBot.Identity))!.ToChatMessage(TestBot.Identity);

        Assert.Equal(message, restored);
        Assert.Equal(7, restored.TelegramId);
        Assert.Equal("photo", restored.PhotoFileId);
    }

    [Fact]
    public async Task AccessList_ShouldKeepOtherBotsEntries()
    {
        var path = Path.Combine(_directory, "allowed_chats");
        await File.WriteAllTextAsync(path, """["-100","5$ew_pidorbot","6$ew_vovabot","1__2"]""");
        using var access = new AccessList(NullLogger<AccessList>.Instance, path, "ew_pidorbot");

        Assert.True(await access.IsAllowedAsync(new(-100)));
        Assert.True(await access.IsAllowedAsync(new(5)));
        Assert.False(await access.IsAllowedAsync(new(6)));
        Assert.Equal([new ChatRef(-100)], await access.GetAllowedGroupsAsync());

        Assert.True(await access.AllowAsync(new(-200)));
        Assert.False(await access.AllowAsync(new(-200)));
        Assert.True(await access.DisallowAsync(new(5)));

        var saved = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path))!;
        Assert.Equal(["-100", "6$ew_vovabot", "1__2", "-200"], saved);
    }

    [Fact]
    public async Task ReminderBook_ShouldReadOldFiles_AndRescheduleDailyReminders()
    {
        var directory = Path.Combine(_directory, "reminders");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "-100---ew_pidorbot"),
            """[{"Id":"a","ChatId":"-100","BotId":"ew_pidorbot","AtUtc":"2026-10-27T09:00:00Z","Request":"пить таблетки","EveryDay":true,"From":"Ivan_Petrov"},{"Id":"b","ChatId":"-100","BotId":"ew_pidorbot","AtUtc":"2026-10-30T09:00:00Z","Request":"later","EveryDay":false,"From":null}]""");
        await File.WriteAllTextAsync(Path.Combine(directory, "-100---ew_vovabot"), "[]");
        using var book = new ReminderBook(NullLogger<ReminderBook>.Instance, directory, "ew_pidorbot");

        var due = await book.TakeDueAsync(TestBot.Start.UtcDateTime);

        Assert.Equal("пить таблетки", Assert.Single(due).Request);
        var left = await book.GetAsync(new(-100));
        Assert.Equal(2, left.Count);
        Assert.Contains(left, reminder => reminder.Id == "a" && reminder.AtUtc == new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));

        // Saved back in the old format.
        using var reloaded = new ReminderBook(NullLogger<ReminderBook>.Instance, directory, "ew_pidorbot");
        Assert.Equal(2, (await reloaded.GetAsync(new(-100))).Count);
    }

    [Theory]
    [InlineData("""{"ivan":"Ваня"}""")]
    [InlineData("ivan=Ваня,broken,ivan=Duplicate")]
    public void Names_ShouldSupportBothFormats(string value)
        => Assert.Equal("Ваня", RedisNameStore.Parse(value).ToDictionary()["ivan"]);
}

public sealed class SmallThingsTests
{
    [Theory]
    [InlineData("hungover and sleepy", "hungover and sleepy.")]
    [InlineData("\"Your mood right now: furious.\"\nbecause", "furious.")]
    [InlineData("Извините, я не могу", null)]
    [InlineData("", null)]
    public void Mood_ShouldBeSanitized(string mood, string? expected)
        => Assert.Equal(expected, MoodKeeper.Sanitize(mood));

    [Fact]
    public void Memory_ShouldUpdate_WhenEnoughHappened()
    {
        var now = TestBot.Start.UtcDateTime;
        var memory = new ChatMemory("ЛЮДИ:", now, now);

        Assert.False(MemoryKeeper.ShouldUpdate(null, MemoryKeeper.MessagesForFirstMemory - 1, now));
        Assert.True(MemoryKeeper.ShouldUpdate(null, MemoryKeeper.MessagesForFirstMemory, now));
        Assert.False(MemoryKeeper.ShouldUpdate(memory, MemoryKeeper.MessagesForUpdate - 1, now));
        Assert.True(MemoryKeeper.ShouldUpdate(memory, MemoryKeeper.MessagesForUpdate, now));
        Assert.True(MemoryKeeper.ShouldUpdate(memory, MemoryKeeper.MessagesForLateUpdate, now + MemoryKeeper.LateUpdateAfter));
    }

    [Fact]
    public void Telegram_ShouldSplitLongMessages()
    {
        var text = string.Join('\n', Enumerable.Repeat(new string('а', 100), 100));

        var parts = TelegramMessenger.Split(text).ToList();

        Assert.True(parts.Count > 1);
        Assert.All(parts, part => Assert.True(part.Length <= 4000));
        Assert.Equal(text.Replace("\n", string.Empty), string.Concat(parts).Replace("\n", string.Empty));
    }

    [Fact]
    public void Telegram_ShouldEscapeMarkdown_ButKeepFormatting()
        => Assert.Equal(@"*жирный* \- точка\.", TelegramMessenger.EscapeMarkdown("*жирный* - точка."));

    [Fact]
    public void Modes_ShouldBeFoundByName()
    {
        Assert.Equal(Modes.Hardcore, Modes.Find(" HardCore "));
        Assert.Null(Modes.Find("unknown"));
        Assert.All(Modes.All, mode => Assert.DoesNotContain('\n', mode.Directive));
    }

    [Fact]
    public void Models_ShouldBeParsed()
    {
        Assert.Equal(new LlmModel(LlmProvider.Grok, "grok-4"), LlmModel.Parse("grok/grok-4"));
        Assert.Equal(new LlmModel(LlmProvider.OpenAI, "gpt-5.4-mini"), LlmModel.Parse("OpenAI/gpt-5.4-mini"));
        Assert.Throws<InvalidOperationException>(() => LlmModel.Parse("gpt"));
    }
}

public sealed class ServiceRegistrationTests
{
    [Fact]
    public async Task AllServices_ShouldBeResolvable()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddPidorBot(
                new Settings { TelegramBotToken = "1:token", OpenAIKey = "key", GrokApiKey = "key", RedisConnectionString = "localhost:1" },
                TestBot.Identity,
                new global::Telegram.Bot.TelegramBotClient("1:token"));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.Equal(3, provider.GetServices<IHostedService>().Count());
    }
}
