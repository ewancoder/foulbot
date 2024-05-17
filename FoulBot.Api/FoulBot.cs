using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api
{
    public interface IFoulBot
    {
        ValueTask HandleUpdateAsync(ITelegramBotClient botClient, Update update);
    }

    public sealed class TypingImitator : IDisposable
    {
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ITelegramBotClient _client;
        private readonly ChatId _chat;
        private readonly Task _typing;
        private readonly DateTime _startedAt;
        private readonly ChatAction _action;
        private string _text = null;

        public TypingImitator(ITelegramBotClient client, ChatId chat, ChatAction action)
        {
            _action = action;
            _client = client;
            _chat = chat;

            _startedAt = DateTime.UtcNow;
            _typing = ImitateTypingAsync();
        }

        private async Task ImitateTypingAsync()
        {
            var random = new Random();

            while (_text == null && DateTime.UtcNow - _startedAt < TimeSpan.FromMinutes(1))
            {
                await _client.SendChatActionAsync(_chat, _action);
                try
                {
                    await Task.Delay(random.Next(300, 10000), _cts.Token);
                }
                catch { }
            }

            var timeSeconds = TimeSpan.FromSeconds(Convert.ToInt32(Math.Floor(60m * ((decimal)_text.Length / 1000m))));

            var remainingSeconds = (timeSeconds - (DateTime.UtcNow - _startedAt)).TotalSeconds;
            while (remainingSeconds > 1)
            {
                remainingSeconds = (timeSeconds - (DateTime.UtcNow - _startedAt)).TotalSeconds;
                if (remainingSeconds <= 0)
                    remainingSeconds = 1;

                await _client.SendChatActionAsync(_chat, _action);
                await Task.Delay(random.Next(2000, Convert.ToInt32(Math.Floor(Math.Min(10000, remainingSeconds))) + 2000));
            }
        }

        public Task FinishTypingText(string text)
        {
            _text = text;
            _cts.Cancel();
            return _typing;
        }

        public void Dispose()
        {
            try
            {
                FinishTypingText(" ");
                _cts.Dispose();
            }
            catch { }
        }
    }

    public sealed class FoulBot
    {
        private readonly string _chat;
        private readonly string _botName;
        private readonly DebugMode _debugMode;
        private readonly IEnumerable<string> _keyWords;
        private readonly bool _listenToConversation;
        private readonly FoulAIClient _foulAIClient;
        private readonly int _messagesBetweenAudio;
        private readonly bool _useConsoleInsteadOfTelegram;
        private readonly Random _random = new Random();
        private readonly int _replyEveryMessages;
        private readonly Task _replyByTime;
        private readonly bool _useOnlyVoice;
        private ITelegramBotClient? _cachedBotClient;
        private int _audioCounter = 0;
        private int _replyEveryMessagesCounter = 0;

        public FoulBot(
            ITelegramBotClient botClient,
            string chat,
            string botName,
            DebugMode debugMode,
            IEnumerable<string> keyWords,
            bool listenToConversation,
            FoulAIClient foulAIClient,
            int messagesBetweenAudio,
            int replyEveryMessages,
            bool useConsoleInsteadOfTelegram,
            bool useOnlyVoice)
        {
            _chat = chat;
            _botName = botName;
            _debugMode = debugMode;
            _keyWords = keyWords;
            _listenToConversation = listenToConversation;
            _foulAIClient = foulAIClient;
            _messagesBetweenAudio = messagesBetweenAudio;
            _cachedBotClient = botClient;
            _useConsoleInsteadOfTelegram = useConsoleInsteadOfTelegram;
            _replyEveryMessages = replyEveryMessages;
            _useOnlyVoice = useOnlyVoice;

            _replyByTime = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                //await SendPersonalMessageAsync();

                while (true)
                {
                    var minutes = _random.Next(20, 120);
                    await Task.Delay(TimeSpan.FromMinutes(minutes));
                    await SendPersonalMessageIfNeededAsync(minutes);
                }
            });
        }

        public async ValueTask HandleUpdateAsync(ITelegramBotClient botClient, Update update)
        {
            _cachedBotClient = botClient;

            if (!UpdateHasMessage(update))
                return;

            var chatId = update.Message!.Chat.Id;
            if (chatId.ToString() != _chat)
                throw new InvalidOperationException("Chat ID doesn't match."); // Log this.

            if (update.Message.Text == $"{_botName} clear context")
            {
                _foulAIClient.ClearContext();
                await botClient.SendTextMessageAsync(chatId, "Context cleared"/*, parseMode: ParseMode.Markdown*/);
                return;
            }

            if (update.Message.Text.StartsWith($"{_botName} change directive "))
            {
                var directive = update.Message.Text.Replace($"{_botName} change directive ", "");
                _foulAIClient.ChangeDirective(directive);
                await botClient.SendTextMessageAsync(chatId, "Changed directive, context cleared"/*, parseMode: ParseMode.Markdown*/);
                return;
            }

            if (update.Message.Text == $"{_botName} get directive")
            {
                var directive = _foulAIClient.GetDirective();
                await botClient.SendTextMessageAsync(chatId, $"Current directive: {directive}"/*, parseMode: ParseMode.Markdown*/);
                return;
            }

            var reasonResponse = NeedToReply(update);

            if (!reasonResponse.Item1)
            {
                if (_listenToConversation)
                    await _foulAIClient.AddToContextAsync(GetUserName(update.Message!.From!), update.Message!.Text!);

                return;
            }

            var reason = reasonResponse.Item2;

            var delay = _random.Next(1, 100);
            if (delay > 90)
            {
                delay = _random.Next(1000, 3000);
            }
            else
            {
                delay = _random.Next(250, 1200);
            }

            await Task.Delay(delay);

            var action = ChatAction.Typing;
            if (_messagesBetweenAudio > 0)
                _audioCounter++;
            if (_audioCounter > _messagesBetweenAudio || _useOnlyVoice)
                action = ChatAction.RecordVoice;
            using var typing = new TypingImitator(botClient, chatId, action);
            try
            {
                if (_replyEveryMessages > 0)
                    _replyEveryMessagesCounter++;

                if (_audioCounter > _messagesBetweenAudio || _useOnlyVoice)
                {
                    _audioCounter = 0;

                    string item2 = null;
                    if (_useConsoleInsteadOfTelegram)
                    {
                        Console.WriteLine(update.Message!.Text!);
                    }
                    else if (_useOnlyVoice == false)
                    {
                        var textResponse = await _foulAIClient.GetTextResponseAsync(GetUserName(update.Message!.From!), update.Message!.Text!);
                        Tuple<System.IO.Stream, string> stream = new(await _foulAIClient.GenerateSpeechAsync(textResponse.Item1), $"{textResponse.Item2}, + {_foulAIClient.GetAudioCents(textResponse.Item1)} cents for audio");

                        await typing.FinishTypingText(textResponse.Item1);

                        item2 = stream.Item2;
                        await botClient.SendVoiceAsync(chatId, InputFile.FromStream(stream.Item1));

                        if (_debugMode == DebugMode.Message)
                            await botClient.SendTextMessageAsync(chatId, $"{stream.Item2} - {reason}"/*, parseMode: ParseMode.Markdown*/);
                    }
                    else
                    {
                        var textResponse = await _foulAIClient.GetTextResponseAsync(GetUserName(update.Message!.From!), update.Message!.Text!);
                        var stream = await new GoogleTtsService().GetAudioAsync(textResponse.Item1);
                        item2 = textResponse.Item2;

                        await typing.FinishTypingText(textResponse.Item1);
                        await botClient.SendVoiceAsync(chatId, InputFile.FromStream(stream));

                        if (_debugMode == DebugMode.Message)
                            await botClient.SendTextMessageAsync(chatId, $"{textResponse.Item2} - {reason}"/*, parseMode: ParseMode.Markdown*/);
                    }

                    if (_debugMode == DebugMode.Console)
                    {
                        Console.WriteLine($"\n\nChat: {chatId}");
                        WriteContext();
                        Console.WriteLine($"{DateTime.UtcNow}\nAUDIO\n{item2} - {reason}");
                        Console.WriteLine("\n\n");
                    }

                    return;
                }

                var text = await _foulAIClient.GetTextResponseAsync(GetUserName(update.Message!.From!), update.Message!.Text!);

                if (_useConsoleInsteadOfTelegram)
                {
                    Console.WriteLine(text);
                }
                else
                {
                    await typing.FinishTypingText(text.Item1);
                    await botClient.SendTextMessageAsync(chatId, _debugMode == DebugMode.Message ? $"{text.Item1}\n\n({text.Item2}) - {reason}" : text.Item1/*, parseMode: ParseMode.Markdown*/);
                }

                if (_debugMode == DebugMode.Console)
                {
                    Console.WriteLine($"\n\nChat: {chatId}");
                    WriteContext();
                    Console.WriteLine($"{DateTime.UtcNow}\n{text.Item1}\n{text.Item2} - {reason}");
                    Console.WriteLine("\n\n");
                }
            }
            catch
            {
                typing.FinishTypingText(" ");
                throw;
            }
        }

        private string GetUserName(User user)
        {
            var name = string.Empty;
            if (user.FirstName != null)
                name += user.FirstName;

            if (user.LastName != null)
                name = string.IsNullOrEmpty(name) ? user.LastName : $"{user.FirstName}_{user.LastName}";

            // TODO: Filter the output so it adheres to telegram Name pattern.

            return name;
        }

        private void WriteContext()
        {
            foreach (var item in _foulAIClient.ContextInfo)
            {
                Console.WriteLine(item);
            }
        }

        private async Task SendPersonalMessageIfNeededAsync(int timePassedMinutes)
        {
            if (!_listenToConversation || _cachedBotClient == null)
                return;

            var text = await _foulAIClient.GetPersonalTextResponseAsync();
            if (text.Item1 == null)
                return;

            if (_useConsoleInsteadOfTelegram)
            {
                Console.WriteLine(text);
            }
            else
            {
                await _cachedBotClient.SendTextMessageAsync(_chat, _debugMode == DebugMode.Message ? $"{text.Item1}\n\n({text.Item2}) - Based on Time passed - {timePassedMinutes} minutes" : text.Item1/*, parseMode: ParseMode.Markdown*/);
            }

            if (_debugMode == DebugMode.Console)
            {
                Console.WriteLine($"\n\nChat: {_chat}");
                WriteContext();
                Console.WriteLine($"{DateTime.UtcNow}\n{text.Item1}\n{text.Item2} - Based on Time passed - {timePassedMinutes} minutes");
                Console.WriteLine("\n\n");
            }
        }

        private bool UpdateHasMessage(Update update)
        {
            if (update.Type != UpdateType.Message)
                return false;

            if (update.Message?.Text == null)
                return false;

            if (update.Message.From == null || (update.Message.From.FirstName == null && update.Message.From.LastName == null))
                return false;

            return true;
        }

        private Tuple<bool, string> NeedToReply(Update update)
        {
            if (update.Message?.ReplyToMessage?.From?.Username == _botName)
                return new (true, "Reply");

            if (update.Message?.Text != null)
            {
                if (_keyWords.Any(keyWord => update.Message.Text.ToLowerInvariant().Contains(keyWord.ToLowerInvariant().Trim())))
                    return new (true, $"Keyword: {_keyWords.First(keyWord => update.Message.Text.ToLowerInvariant().Contains(keyWord.ToLowerInvariant().Trim()))}");
            }

            if (_replyEveryMessages > 0)
                _replyEveryMessagesCounter++;

            if (_replyEveryMessagesCounter > _replyEveryMessages && _random.Next(0, 10) > 7)
            {
                var messagesCount = _replyEveryMessagesCounter;
                _replyEveryMessagesCounter = 0;
                return new (true, $"Messages Count: {messagesCount}");
            }

            return new(false, null);
        }
    }
}
