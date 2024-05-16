using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FoulBot.Api
{
    public sealed class FoulBotGatewayFactory
    {
        private readonly string _aiApiKey;
        private readonly DebugMode _debugMode;
        private readonly bool _useConsoleInsteadOfTelegram;

        public FoulBotGatewayFactory(
            string aiApiKey,
            DebugMode debugMode,
            bool useConsoleInsteadOfTelegram)
        {
            _aiApiKey = aiApiKey;
            _debugMode = debugMode;
            _useConsoleInsteadOfTelegram = useConsoleInsteadOfTelegram;
        }

        public FoulBotGateway CreateFoulBot(
            string botApiKey, string botName, IEnumerable<string> keyWords,
            string mainDirective,
            bool listenToConversation = true,
            int replyEveryMessages = 20,
            int maxMessagesInContext = 10,
            int messagesBetweenAudio = 0)
        {
            return new FoulBotGateway(
                _debugMode,
                botApiKey,
                _aiApiKey,
                botName,
                keyWords,
                listenToConversation,
                maxMessagesInContext,
                mainDirective,
                messagesBetweenAudio,
                _useConsoleInsteadOfTelegram,
                replyEveryMessages);
        }
    }

    public sealed class FoulBotGateway : IUpdateHandler
    {
        private readonly List<string> _cachedChats = new List<string>();
        private readonly DateTime _start = DateTime.UtcNow;
        private readonly DebugMode _debugMode;
        private readonly string _aiApiKey;
        private readonly ITelegramBotClient _client;
        private readonly string _botName;
        private readonly ConcurrentDictionary<string, FoulBot> _botPool
            = new ConcurrentDictionary<string, FoulBot>();
        private readonly IEnumerable<string> _keyWords;
        private readonly bool _listenToConversation;
        private readonly int _maxMessagesInContext;
        private readonly int _messagesBetweenAudio;
        private readonly bool _useConsoleInsteadOfTelegram;
        private readonly int _replyEveryMessages;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private string _mainDirective;

        public FoulBotGateway(
            DebugMode debugMode,
            string botApiKey,
            string aiApiKey,
            string botName,
            IEnumerable<string> keyWords,
            bool listenToConversation,
            int maxMessagesInContext,
            string mainDirective,
            int messagesBetweenAudio,
            bool useConsoleInsteadOfTelegram,
            int replyEveryMessages)
        {
            _replyEveryMessages = replyEveryMessages;
            _debugMode = debugMode;
            _aiApiKey = aiApiKey;
            _botName = botName;
            _keyWords = keyWords;
            _maxMessagesInContext = maxMessagesInContext;
            _listenToConversation = listenToConversation;
            _mainDirective = mainDirective;
            _messagesBetweenAudio = messagesBetweenAudio;
            _useConsoleInsteadOfTelegram = useConsoleInsteadOfTelegram;
            _client = new TelegramBotClient(botApiKey);
            _client.StartReceiving(this, cancellationToken: _cts.Token);
            TryCachingTheChat(null);
            foreach (var chat in _cachedChats)
            {
                _botPool.GetOrAdd(chat, chat => new FoulBot(
                    _client,
                    chat,
                    _botName,
                    _debugMode,
                    _keyWords,
                    _listenToConversation,
                    new FoulAIClient(_aiApiKey, _maxMessagesInContext, _mainDirective),
                    _messagesBetweenAudio,
                    _replyEveryMessages,
                    _useConsoleInsteadOfTelegram));
            }
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                // 10 seconds cold start.
                if (DateTime.UtcNow < _start.AddSeconds(10))
                    return;

                var chat = update.Message?.Chat?.Id.ToString();
                if (chat == null)
                    return;

                TryCachingTheChat(chat);

                var bot = _botPool.GetOrAdd(chat, chat => new FoulBot(
                    _client,
                    chat,
                    _botName,
                    _debugMode,
                    _keyWords,
                    _listenToConversation,
                    new FoulAIClient(_aiApiKey, _maxMessagesInContext, _mainDirective),
                    _messagesBetweenAudio,
                    _replyEveryMessages,
                    _useConsoleInsteadOfTelegram));

                await bot.HandleUpdateAsync(botClient, update);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            // TODO: Log the error.
            return Task.CompletedTask;
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public BotInfo ToBotInfo()
        {
            return new BotInfo(_botName, _mainDirective);
        }

        public void ResetWithNewDirective(string newMainDirective)
        {
            _mainDirective = newMainDirective;
            _botPool.Clear();
        }

        private void TryCachingTheChat(string? chat)
        {
            try
            {
                if (_cachedChats.Count == 0 && System.IO.File.Exists($"{_botName}.chats"))
                    _cachedChats.AddRange(
                        JsonSerializer.Deserialize<IEnumerable<string>>(
                            System.IO.File.ReadAllText($"{_botName}.chats")));

                if (chat != null && !_cachedChats.Contains(chat))
                {
                    _cachedChats.Add(chat);
                    System.IO.File.WriteAllText($"{_botName}.chats", JsonSerializer.Serialize(_cachedChats));
                }
            } catch
            {
                Console.WriteLine("Could not cache/read chats.");
            }
        }
    }
}
