using FoulBot.Domain.Storage;

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
    private readonly ILogger<FoulBot> _foulBotLogger;
    private readonly ILogger<ReplyImitator> _typingImitatorLogger;
    private readonly ILogger<ReminderFeature> _remindersFeatureLogger;
    private readonly ILogger<BotReplyStrategy> _botReplyStrategyLogger;
    private readonly ILogger<TalkYourselfFeature> _talkYourselfFeatureLogger;

    public FoulBotFactory(
        TimeProvider timeProvider,
        IBotDelayStrategy botDelayStrategy,
        ISharedRandomGenerator random,
        IFoulAIClientFactory aiClientFactory,
        IReminderStore reminderStore,
        ILogger<FoulBot> foulBotLogger,
        ILogger<ReplyImitator> typingImitatorLogger,
        ILogger<ReminderFeature> reminderCreatorLogger,
        ILogger<BotReplyStrategy> botReplyStrategyLogger,
        ILogger<TalkYourselfFeature> talkYourselfFeatureLogger)
    {
        _timeProvider = timeProvider;
        _delayStrategy = botDelayStrategy;
        _random = random;
        _aiClientFactory = aiClientFactory;
        _reminderStore = reminderStore;
        _foulBotLogger = foulBotLogger;
        _typingImitatorLogger = typingImitatorLogger;
        _remindersFeatureLogger = reminderCreatorLogger;
        _botReplyStrategyLogger = botReplyStrategyLogger;
        _talkYourselfFeatureLogger = talkYourselfFeatureLogger;
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
        var messenger = new ChatScopedBotMessenger(botMessenger, chat.ChatId);
        var replyStrategy = new BotReplyStrategy(_botReplyStrategyLogger, _timeProvider, chat, config);
        var typingImitatorFactory = new ReplyImitatorFactory(
            _typingImitatorLogger, botMessenger, _timeProvider, _random);
        var aiClient = _aiClientFactory.Create(config.OpenAIModel);
        IMessageFilter messageFilter = config.IsAssistant
            ? new AssistantMessageFilter()
            : new FoulMessageFilter();
        var replyModePicker = new BotReplyModePicker(config);

        var bot = new FoulBot(
            _foulBotLogger,
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
        var remindersFeature = new ReminderFeature(
            _remindersFeatureLogger,
            _timeProvider,
            _reminderStore,
            config,
            chat.ChatId,
            bot,
            cts.Token); // Bot graceful shutdown will not straightaway call cancellation. We want to make sure it happens.

        bot.AddFeature(remindersFeature);

        if (config.TalkOnYourOwn && !chat.IsPrivateChat)
        {
            var talkYourselfFeature = new TalkYourselfFeature(
                _talkYourselfFeatureLogger,
                _timeProvider,
                _random,
                bot,
                config);

            bot.AddFeature(talkYourselfFeature);
        }

        return bot;
    }
}
