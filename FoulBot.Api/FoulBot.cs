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
        private readonly bool _isDebug;
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
            bool isDebug,
            IEnumerable<string> keyWords,
            bool listenToConversation,
            IFoulAIClient foulAIClient,
            int messagesBetweenAudio,
            int replyEveryMessages,
            bool useConsoleInsteadOfTelegram)
        {
            _chat = chat;
            _botName = botName;
            _isDebug = isDebug;
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
                await SendPersonalMessageAsync();

                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(_random.Next(20, 120)));
                    await SendPersonalMessageAsync();
                }
            });
        }

        public async ValueTask HandleUpdateAsync(ITelegramBotClient botClient, Update update)
        {
            _cachedBotClient = botClient;

            if (!UpdateHasMessage(update))
                return;

            if (!NeedToReply(update))
            {
                if (_listenToConversation)
                    await _foulAIClient.AddToContextAsync(update.Message!.From!.FirstName, update.Message!.Text!);

                return;
            }

            if (_messagesBetweenAudio > 0)
                _audioCounter++;
            if (_replyEveryMessages > 0)
                _replyEveryMessagesCounter++;

            var chatId = update.Message!.Chat.Id;
            if (chatId.ToString() != _chat)
                throw new InvalidOperationException("Chat ID doesn't match."); // Log this.

            if (_audioCounter > _messagesBetweenAudio)
            {
                _audioCounter = 0;

                if (_useConsoleInsteadOfTelegram)
                {
                    Console.WriteLine(update.Message!.Text!);
                }
                else
                {
                    var stream = await _foulAIClient.GetAudioResponseAsync(update.Message!.From!.FirstName, update.Message!.Text!);
                    await botClient.SendVoiceAsync(chatId, InputFile.FromStream(stream.Item1));
                    await botClient.SendTextMessageAsync(chatId, stream.Item2);
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
                await botClient.SendTextMessageAsync(chatId, _isDebug ? $"{text.Item1}\n\n({text.Item2})" : text.Item1);
            }
        }

        private async Task SendPersonalMessageAsync()
        {
            if (!_listenToConversation || _cachedBotClient == null)
                return;

            var text = await _foulAIClient.GetPersonalTextResponseAsync();

            if (_useConsoleInsteadOfTelegram)
            {
                Console.WriteLine(text);
            }
            else
            {
                await _cachedBotClient.SendTextMessageAsync(_chat, _isDebug ? $"{text.Item1}\n\n({text.Item2})" : text.Item1);
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

        private bool NeedToReply(Update update)
        {
            if (update.Message?.ReplyToMessage?.From?.Username == _botName)
                return true;

            if (update.Message?.Text != null)
            {
                if (_keyWords.Any(keyWord => update.Message.Text.ToLowerInvariant().Contains(keyWord.ToLowerInvariant().Trim())))
                    return true;
            }

            if (_replyEveryMessages > 0)
                _replyEveryMessagesCounter++;

            if (_replyEveryMessagesCounter > _replyEveryMessages && _random.Next(0, 10) > 7)
            {
                _replyEveryMessagesCounter = 0;
                return true;
            }

            return false;
        }
    }
}
