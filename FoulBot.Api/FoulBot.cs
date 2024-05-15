using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

    public sealed class FoulBot
    {
        private readonly string _chat;
        private readonly string _botName;
        private readonly DebugMode _debugMode;
        private readonly IEnumerable<string> _keyWords;
        private readonly bool _listenToConversation;
        private readonly IFoulAIClient _foulAIClient;
        private readonly int _messagesBetweenAudio;
        private readonly bool _useConsoleInsteadOfTelegram;
        private readonly Random _random = new Random();
        private readonly int _replyEveryMessages;
        private readonly Task _replyByTime;
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
            IFoulAIClient foulAIClient,
            int messagesBetweenAudio,
            int replyEveryMessages,
            bool useConsoleInsteadOfTelegram)
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

            _replyByTime = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                //await SendPersonalMessageAsync();

                while (true)
                {
                    var minutes = _random.Next(20, 120);
                    await Task.Delay(TimeSpan.FromMinutes(minutes));
                    await SendPersonalMessageAsync(minutes);
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
                await botClient.SendTextMessageAsync(chatId, "Context cleared", parseMode: ParseMode.Markdown);
                return;
            }

            if (update.Message.Text.StartsWith($"{_botName} change directive "))
            {
                var directive = update.Message.Text.Replace($"{_botName} change directive ", "");
                _foulAIClient.ChangeDirective(directive);
                await botClient.SendTextMessageAsync(chatId, "Changed directive, context cleared", parseMode: ParseMode.Markdown);
                return;
            }

            if (update.Message.Text == $"{_botName} get directive")
            {
                var directive = _foulAIClient.GetDirective();
                await botClient.SendTextMessageAsync(chatId, $"Current directive: {directive}", parseMode: ParseMode.Markdown);
                return;
            }

            var reasonResponse = NeedToReply(update);

            if (!reasonResponse.Item1)
            {
                if (_listenToConversation)
                    await _foulAIClient.AddToContextAsync(update.Message!.From!.FirstName, update.Message!.Text!);

                return;
            }

            var reason = reasonResponse.Item2;
            if (string.IsNullOrEmpty(reason))
            {
            }

            if (_messagesBetweenAudio > 0)
                _audioCounter++;
            if (_replyEveryMessages > 0)
                _replyEveryMessagesCounter++;

            if (_audioCounter > _messagesBetweenAudio)
            {
                _audioCounter = 0;

                string item2 = null;
                if (_useConsoleInsteadOfTelegram)
                {
                    Console.WriteLine(update.Message!.Text!);
                }
                else
                {
                    var stream = await _foulAIClient.GetAudioResponseAsync(update.Message!.From!.FirstName, update.Message!.Text!);
                    item2 = stream.Item2;
                    await botClient.SendVoiceAsync(chatId, InputFile.FromStream(stream.Item1));

                    if (_debugMode == DebugMode.Message)
                        await botClient.SendTextMessageAsync(chatId, $"{stream.Item2} - {reason}", parseMode: ParseMode.Markdown);
                }

                if (_debugMode == DebugMode.Console)
                {
                    Console.WriteLine($"\n\n{DateTime.UtcNow}\nAUDIO\n{item2} - {reason}");
                    WriteContext();
                    Console.WriteLine("\n\n");
                }

                return;
            }

            var text = await _foulAIClient.GetTextResponseAsync(update.Message!.From!.FirstName, update.Message!.Text!);

            if (_useConsoleInsteadOfTelegram)
            {
                Console.WriteLine(text);
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, _debugMode == DebugMode.Message ? $"{text.Item1}\n\n({text.Item2}) - {reason}" : text.Item1, parseMode: ParseMode.Markdown);
            }

            if (_debugMode == DebugMode.Console)
            {
                Console.WriteLine($"\n\n{DateTime.UtcNow}\n{text.Item1}\n{text.Item2} - {reason}");
                WriteContext();
                Console.WriteLine("\n\n");
            }
        }

        private void WriteContext()
        {
            foreach (var item in _foulAIClient.ContextInfo)
            {
                Console.WriteLine(item);
            }
        }

        private async Task SendPersonalMessageAsync(int timePassedMinutes)
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
                await _cachedBotClient.SendTextMessageAsync(_chat, _debugMode == DebugMode.Message ? $"{text.Item1}\n\n({text.Item2}) - Based on Time passed - {timePassedMinutes} minutes" : text.Item1, parseMode: ParseMode.Markdown);
            }

            if (_debugMode == DebugMode.Console)
            {
                Console.WriteLine($"\n\n{DateTime.UtcNow}\n{text.Item1}\n{text.Item2} - Based on Time passed - {timePassedMinutes} minutes");
                WriteContext();
                Console.WriteLine("\n\n");
            }
        }

        private bool UpdateHasMessage(Update update)
        {
            if (update.Type != UpdateType.Message)
                return false;

            if (update.Message?.Text == null)
                return false;

            if (update.Message.From == null || update.Message.From.FirstName == null)
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
