using PidorBot.AI;
using PidorBot.Brain;
using PidorBot.Chats;

namespace PidorBot.Tests;

public sealed class ChatSessionTests : IDisposable
{
    private static readonly ChatRef _group = new(-100500);
    private static readonly ChatRef _private = new(100500);
    private readonly TestBot _bot = new();

    public void Dispose() => _bot.Dispose();

    [Fact]
    public async Task ShouldReply_WhenTriggered()
    {
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, ты где?"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal([_bot.Llm.DefaultText], _bot.Messenger.Texts);
        Assert.Contains(session.GetHistory(), message => message.IsFromBot && message.Text == _bot.Llm.DefaultText);
        Assert.Contains(_bot.Store.History[_group], message => message.IsFromBot);
    }

    [Fact]
    public async Task ShouldNotReply_WhenNotAddressed()
    {
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("просто болтаем"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Empty(_bot.Llm.Calls);
        Assert.Empty(_bot.Messenger.Texts);
    }

    [Fact]
    public async Task ShouldReplyToEverything_InPrivateChat()
    {
        var session = await CreateAsync(_private);

        await session.HandleAsync(TestBot.Message("просто болтаем"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Single(_bot.Messenger.Texts);
    }

    [Fact]
    public async Task ShouldNotReplyToBacklog()
    {
        var session = await CreateAsync(_private);

        await session.HandleAsync(TestBot.Message("бот, ты где?"), isBacklog: true);
        await session.ReplyIfNeededAsync();

        Assert.Empty(_bot.Messenger.Texts);
        Assert.Single(session.GetHistory());
    }

    [Fact]
    public async Task ShouldNotReplyTwice_ToTheSameMessages()
    {
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, ты где?"), isBacklog: false);
        await session.ReplyIfNeededAsync();
        await session.ReplyIfNeededAsync();

        Assert.Single(_bot.Messenger.Texts);
    }

    [Fact]
    public async Task ShouldReplyToKeyword_OncePerCooldown()
    {
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("работа достала", 1), isBacklog: false);
        await session.ReplyIfNeededAsync();
        await session.HandleAsync(TestBot.Message("опять работа", 2), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Single(_bot.Messenger.Texts);

        _bot.Time.Advance(ReplyPolicy.KeywordCooldown);
        await session.HandleAsync(TestBot.Message("работа", 3), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal(2, _bot.Messenger.Texts.Count);
    }

    [Fact]
    public async Task ShouldToggleHardcoreMode_AndPersistIt()
    {
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("$pidormode"), isBacklog: false);

        Assert.Equal(Modes.Hardcore, session.Mode);
        Assert.Equal("hardcore", _bot.Store.Modes[_group]);
        Assert.Contains("hardcore", _bot.Messenger.Texts.Single());
        Assert.Empty(session.GetHistory()); // Commands are not part of the conversation.

        await session.HandleAsync(TestBot.Message("$pidormode"), isBacklog: false);
        Assert.Equal(Modes.Mild, session.Mode);

        await session.HandleAsync(TestBot.Message("$mode hardcore"), isBacklog: false);
        Assert.Equal(Modes.Hardcore, (await CreateAsync(_group)).Mode);
    }

    [Fact]
    public async Task ShouldUseHardcoreDirective_InHardcoreMode()
    {
        _bot.Store.Modes[_group] = "hardcore";
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, привет"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.StartsWith(Modes.Hardcore.Directive, _bot.Llm.Calls.Single().Messages[0].Text);
    }

    [Fact]
    public async Task ShouldReact_WhenTheModelDecidesTo()
    {
        _bot.Llm.Respond(string.Empty, Call(BotTools.React, """{ "message_id": 7, "emoji": "🤡" }"""));
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, я красавчик", 7), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal([(7, "🤡")], _bot.Messenger.Reactions);
        // The model sees the result of the tool and can continue.
        Assert.Equal(LlmRole.Tool, _bot.Llm.Calls[1].Messages[^1].Role);
    }

    [Fact]
    public async Task ShouldNotReact_ToUnknownMessage()
    {
        _bot.Llm.Respond(string.Empty, Call(BotTools.React, """{ "message_id": 999, "emoji": "🤡" }"""));
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, я красавчик", 7), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Empty(_bot.Messenger.Reactions);
        Assert.StartsWith("Error", _bot.Llm.Calls[1].Messages[^1].Text);
    }

    [Fact]
    public async Task ShouldGlance_AndOnlyReact_WhenNotAddressed()
    {
        using var bot = new TestBot(glanceChance: 1);
        bot.Llm.Respond("this text is ignored", Call(BotTools.React, """{ "message_id": 3, "emoji": "🔥" }"""));
        var session = await ChatSession.CreateAsync(_group, bot.Services);

        await session.HandleAsync(TestBot.Message("просто болтаем", 3), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal([(3, "🔥")], bot.Messenger.Reactions);
        Assert.Empty(bot.Messenger.Texts);
        Assert.Equal([BotTools.React], bot.Llm.Calls[0].Tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task ShouldSetReminder_FromHumanSpeech()
    {
        _bot.Llm
            .Respond("Напомню, куда я денусь.", Call(BotTools.SetReminder, """{ "time": "2026-10-28 09:00", "what": "Ивану купить пиво" }"""))
            .Respond(string.Empty);
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, напомни мне завтра в 9 купить пиво"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        var reminder = Assert.Single(await _bot.Reminders.GetAsync(_group));
        Assert.Equal(new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc), reminder.AtUtc);
        Assert.Equal("Ивану купить пиво", reminder.Request);
        Assert.Equal("Ivan_Petrov", reminder.RequestedBy);
        Assert.Equal(["Напомню, куда я денусь."], _bot.Messenger.Texts);
    }

    [Fact]
    public async Task ShouldNotSetReminder_InThePast()
    {
        _bot.Llm.Respond(string.Empty, Call(BotTools.SetReminder, """{ "time": "2020-01-01 09:00", "what": "x" }"""));
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, напомни"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Empty(await _bot.Reminders.GetAsync(_group));
    }

    [Fact]
    public async Task ShouldSendStickerAndImage()
    {
        _bot.Stickers.Known.Add(new("sticker-file", "unique", "😂", "set"));
        _bot.Llm.Respond(
            string.Empty,
            Call(BotTools.SendSticker, """{ "emoji": "😂" }"""),
            Call(BotTools.SendImage, """{ "prompt": "a grumpy robot", "caption": "это ты" }"""));
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, нарисуй меня"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal(["sticker-file"], _bot.Messenger.Stickers);
        Assert.Equal(["a grumpy robot"], _bot.Images.Prompts);
        Assert.Equal(["это ты"], _bot.Messenger.Photos);
    }

    [Fact]
    public async Task ShouldRegenerate_WhenTheModelBreaksCharacter()
    {
        _bot.Llm.Respond("Извините, я не могу продолжать этот разговор.").Respond("Отвали.");
        var session = await CreateAsync(_group);

        await session.HandleAsync(TestBot.Message("бот, привет"), isBacklog: false);
        await session.ReplyIfNeededAsync();

        Assert.Equal(["Отвали."], _bot.Messenger.Texts);
    }

    [Fact]
    public async Task ShouldSendVoice_EveryNthReply()
    {
        using var bot = new TestBot(voiceEveryReplies: 1);
        var session = await ChatSession.CreateAsync(_private, bot.Services);

        for (var i = 1; i <= 4; i++)
        {
            await session.HandleAsync(TestBot.Message("привет", i), isBacklog: false);
            await session.ReplyIfNeededAsync();
        }

        Assert.Equal(2, bot.Messenger.Voices);
        Assert.Equal(2, bot.Messenger.Texts.Count);
    }

    [Fact]
    public async Task ShouldRemind()
    {
        var session = await CreateAsync(_group);

        await session.RemindAsync("Ivan_Petrov", "купить пиво");

        Assert.Contains("купить пиво", _bot.Llm.Calls.Single().Messages[^1].Text);
        Assert.Single(_bot.Messenger.Texts);
    }

    private Task<ChatSession> CreateAsync(ChatRef chat) => ChatSession.CreateAsync(chat, _bot.Services);

    private static LlmToolCall Call(string name, string arguments) => new(Guid.NewGuid().ToString(), name, arguments);
}
