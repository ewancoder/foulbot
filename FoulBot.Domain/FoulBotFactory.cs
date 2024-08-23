namespace FoulBot.Domain;

public delegate ValueTask<IFoulBot?> JoinBotToChatAsync(IFoulChat chat);

public interface IFoulBotFactory
{
    ValueTask<IFoulBot?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config);
}

public sealed class FoulBotFactory : IFoulBotFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly IBotDelayStrategy _delayStrategy;
    private readonly ISharedRandomGenerator _random;
    private readonly IFoulAIClientFactory _aiClientFactory;
    private readonly IReminderStore _reminderStore;
    private readonly ILogger<ReplyImitator> _typingImitatorLogger;
    private readonly ILogger<ReminderCommandProcessor> _reminderCreatorLogger;

    public FoulBotFactory(
        TimeProvider timeProvider,
        IBotDelayStrategy botDelayStrategy,
        ISharedRandomGenerator random,
        IFoulAIClientFactory aiClientFactory,
        IReminderStore reminderStore,
        ILogger<ReplyImitator> typingImitatorLogger,
        ILogger<ReminderCommandProcessor> reminderCreatorLogger)
    {
        _timeProvider = timeProvider;
        _delayStrategy = botDelayStrategy;
        _random = random;
        _aiClientFactory = aiClientFactory;
        _reminderStore = reminderStore;
        _typingImitatorLogger = typingImitatorLogger;
        _reminderCreatorLogger = reminderCreatorLogger;
    }

    /// <summary>
    /// Returns null when it's not possible to join this bot to this chat.
    /// </summary>
    public async ValueTask<IFoulBot?> JoinBotToChatAsync(
        IBotMessenger botMessenger,
        IFoulChat chat,
        FoulBotConfiguration config)
    {
        var canWriteToChat = await botMessenger.CheckCanWriteAsync(chat.ChatId);
        if (!canWriteToChat)
            return null;

        var cts = new CancellationTokenSource();
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId, cts.Token);
        var replyStrategy = new BotReplyStrategy(_timeProvider, chat, config);
        var typingImitatorFactory = new ReplyImitatorFactory(
            _typingImitatorLogger, botMessenger, _timeProvider, _random);
        var aiClient = _aiClientFactory.Create(config.OpenAIModel);
        IMessageFilter messageFilter = config.IsAssistant
            ? new AssistantMessageFilter()
            : new FoulMessageFilter();
        var replyModePicker = new BotReplyModePicker(config);

        var bot = new FoulBot(
            messenger,
            _delayStrategy,
            replyStrategy,
            replyModePicker,
            typingImitatorFactory,
            _random,
            aiClient,
            messageFilter,
            chat,
            cts,
            config);

        // Legacy class to be reworked. Currently starts reminders mechanism
        // on creation, so no need to keep the reference.

        // TODO: Come up with a better VISIBLE solution to dispose of it.
#pragma warning disable CA2000 // It is being disposed on bot Shutdown.
        var reminderCreator = new ReminderCommandProcessor(
            _reminderCreatorLogger,
            _reminderStore,
            config,
            chat.ChatId,
            bot,
            cts.Token); // Bot graceful shutdown will not straightaway call cancellation. We want to make sure it happens.

        bot.AddCommandProcessor(reminderCreator);

        return bot;
    }
}
