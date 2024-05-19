using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public interface IFoulBot
{
    string BotId { get; }
    ValueTask<bool> JoinChatAsync(IFoulChat chat, string invitedBy = null);
}

public sealed class FoulBot : IFoulBot
{
    private static readonly string[] _failedContext = [
        "извините", "sorry", "простите", "не могу продолжать",
        "не могу участвовать", "давайте воздержимся", "прошу прощения",
        "за нецензурн", "прошу вас выражаться", "извини", "прости", "не могу помочь",
        "не могу обсуждать"
    ];
    private readonly IFoulAIClient _aiClient;
    private readonly ITelegramBotClient _botClient;
    private readonly Random _random = new Random();
    private readonly string _botIdName;
    private readonly string _botName;
    private readonly List<string> _keyWords;
    private readonly string _directive;
    private readonly int _contextSize;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
    private readonly int _replyEveryMessages;
    private readonly int _messagesBetweenAudio;
    private readonly bool _useOnlyVoice;
    private readonly int _botOnlyMaxCount = 10;
    private readonly int _botOnlyDecrementIntervalSeconds = 60;
    private readonly Task _botOnlyDecrementTask;
    private readonly string[] _stickers;
    private bool _subscribed;
    private int _botOnlyCount = 0;
    private int _audioCounter = 0;
    private int _replyEveryMessagesCounter = 0;
    private IFoulChat _chat;
    private string? _lastProcessedId;

    public FoulBot(
        IFoulAIClient aiClient,
        ITelegramBotClient botClient,
        string directive,
        string botIdName,
        string botName,
        List<string> keyWords,
        int contextSize,
        int replyEveryMessages,
        int messagesBetweenAudio,
        bool useOnlyVoice,
        string[] stickers)
    {
        _stickers = stickers;
        _aiClient = aiClient;
        _botClient = botClient;
        _directive = directive;
        _botIdName = botIdName;
        _botName = botName;
        _keyWords = keyWords;
        _contextSize = contextSize;
        _replyEveryMessages = replyEveryMessages;
        _messagesBetweenAudio = messagesBetweenAudio;
        _useOnlyVoice = useOnlyVoice;
        _botOnlyDecrementTask = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_botOnlyDecrementIntervalSeconds));
                if (_botOnlyCount > 0)
                    _botOnlyCount--;
            }
        });
    }

    public string BotId => _botIdName;

    // TODO: If after restarting the solution someone REMOVES a bot from the chat - all the other bots will comment on it as if you have ADDED them.
    // This is because the chat object has not been created yet and this method is called for everyone.
    public async ValueTask<bool> JoinChatAsync(IFoulChat chat, string invitedBy = null)
    {
        try
        {
            _chat = chat;
            _chat.StatusChanged += OnStatusChanged;

            // Test that bot is a member of the chat by trying to send an event.
            await _botClient.SendChatActionAsync(chat.ChatId, ChatAction.ChooseSticker);

            // Do not send status messages if bot has already been in the chat.
            if (invitedBy != null)
            {
                if (_stickers.Any())
                    await _botClient.SendStickerAsync(chat.ChatId, InputFile.FromFileId(_stickers[_random.Next(0, _stickers.Length)]));

                var welcome = await _aiClient.GetCustomResponseAsync(
$"{_directive}. You have just been added to a chat group with a number of people{(invitedBy == null ? string.Empty : " by a person named "+ invitedBy + "")}, tell them hello in your manner or thank the person for adding you if you feel like it.");
                await _botClient.SendTextMessageAsync(chat.ChatId, welcome);
            }

            Console.WriteLine($"Bot {_botName} joined chat {chat.ChatId}.");
            _chat.MessageReceived += OnMessageReceived;
            _subscribed = true;
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Failed to join {_botClient} to chat {chat.ChatId}");
            return false;
        }
    }

    private void OnMessageReceived(object sender, FoulMessage message) => OnMessageReceivedAsync(message);
    private async Task OnMessageReceivedAsync(FoulMessage message)
    {
        Console.WriteLine($"\nMESSAGE RECEIVED: {message.Text}\n");

        if (_replyEveryMessages > 0)
            _replyEveryMessagesCounter++;

        // TODO: Do NOT send multiple messages in a row if you just sent some. Otherwise it's possible that bot will always talk with itself.
        // BUT if there are MULTIPLE new messages in context apart from yours AND they match your keywords - DO send once more.

        // TODO: Consider not processing the accumulated messages after lock has been released.

        // We don't care about getting it in order - it's getting added there in order, but we just grab the current state at the moment when we receive the message.
        // We can't place it inside the lock or everything will hang because EVERY message will be waiting
        var snapshot = _chat.GetContextSnapshot();

        // Do not allow sending multiple messages. Just skip it till the next one arrives.
        if (snapshot.LastOrDefault() != null && snapshot.Last().SenderName == _botIdName)
            return;

        var unprocessedMessages = snapshot;

        if (_lastProcessedId != null)
        {
            unprocessedMessages = unprocessedMessages
                .SkipWhile(message => message.Id != _lastProcessedId)
                .Skip(1)
                .ToList();
        }

        if (!unprocessedMessages.Any())
        {
            Console.WriteLine("No unprocessed messages. Returning.");
            return;
        }

        if (!unprocessedMessages.Exists(ShouldAct) && _replyEveryMessagesCounter < _replyEveryMessages)
        {
            Console.WriteLine("Exiting because there are no messages that need to be processed.");
            return;
        }

        if (_replyEveryMessagesCounter >= _replyEveryMessages)
        {
            _replyEveryMessagesCounter = 0; // TODO: Test this that bot really sends messages every N messages.
            Console.WriteLine("Reset counter for N messages repeat.");
        }

        if (unprocessedMessages.Last().IsOriginallyBotMessage)
        {
            if (_botOnlyCount >= _botOnlyMaxCount)
            {
                Console.WriteLine($"Exceeded bot-to-bot messages count. Waiting for {_botOnlyDecrementIntervalSeconds} seconds to decrease the count. Meanwhile all messages are lost.");
                return;
            }

            Console.WriteLine("Increasing bot-only count.");
            _botOnlyCount++;
        }

        // We are only going inside the lock when we're sure we've got a message that needs reply from this bot.
        Console.WriteLine("Acquiring lock.");
        await _lock.WaitAsync();
        try
        {
            Console.WriteLine("Inside lock.");
            {
                // After waiting - let's grab context one more time.
                snapshot = _chat.GetContextSnapshot();

                // Do not allow sending multiple messages. Just skip it till the next one arrives.
                if (snapshot.LastOrDefault() != null && snapshot.Last().SenderName == _botIdName)
                    return;

                unprocessedMessages = snapshot;

                if (_lastProcessedId != null)
                {
                    unprocessedMessages = unprocessedMessages
                        .SkipWhile(message => message.Id != _lastProcessedId)
                        .Skip(1)
                        .ToList();
                }

                if (!unprocessedMessages.Exists(ShouldAct) && _replyEveryMessagesCounter < _replyEveryMessages)
                    return;

                if (_replyEveryMessagesCounter >= _replyEveryMessages)
                    _replyEveryMessagesCounter = 0; // TODO: Test this that bot really sends messages every N messages.

                if (unprocessedMessages.Last().IsOriginallyBotMessage)
                {
                    if (_botOnlyCount >= _botOnlyMaxCount)
                    {
                        Console.WriteLine($"Exceeded bot-to-bot messages count. Waiting for {_botOnlyDecrementIntervalSeconds} seconds to decrease the count.");
                        return;
                    }

                    _botOnlyCount++;
                }
            }

            // TODO: This is duplicated code because after we enter the lock we still need to do this (after the previous message got handled).

            /*while (unprocessedMessages[^1].SenderName == _botName)
                unprocessedMessages.RemoveAt(unprocessedMessages.Count - 1);*/

            // Delay to "read" the messages.
            {
                var delay = _random.Next(1, 100);
                if (delay > 90)
                {
                    delay = _random.Next(5000, 20000);
                }
                if (delay <= 90 && delay > 70)
                {
                    delay = _random.Next(1500, 5000);
                }
                if (delay <= 70)
                {
                    delay = _random.Next(200, 1200);
                }
                await Task.Delay(delay);
                snapshot = _chat.GetContextSnapshot();
            }

            // Consider doing that at the very end.
            _lastProcessedId = snapshot[^1].Id;

            var isAudio = false;
            if (_messagesBetweenAudio > 0)
                _audioCounter++;
            if (_audioCounter > _messagesBetweenAudio || _useOnlyVoice)
            {
                isAudio = true;
                _audioCounter = 0;
            }

            using var typing = new TypingImitator(_botClient, _chat.ChatId, isAudio ? ChatAction.RecordVoice : ChatAction.Typing);

            // TODO: Imitate typing.

            // Get context from this bot for the AI client.
            var context = GetContextForAI(snapshot);

            /*var aiGeneratedTextResponse = $"{DateTime.UtcNow} - bot reply from {_botName}";*/
            var aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);
            var contextForLogging = string.Join('\n', context.Select(c => c.ToString()));

            var i = 0;
            while (_failedContext.Any(keyword => aiGeneratedTextResponse.ToLowerInvariant().Contains(keyword.ToLowerInvariant())))
            {
                i++;
                await Task.Delay(1000);
                aiGeneratedTextResponse = await _aiClient.GetTextResponseAsync(context);

                if (i > 5)
                    throw new InvalidOperationException("Could not generate a valid response.");
            }

            if (!_useOnlyVoice)
            {
                if (isAudio)
                {
                    using var stream = await _aiClient.GetAudioResponseAsync(aiGeneratedTextResponse);

                    await typing.FinishTypingText(aiGeneratedTextResponse);
                    await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
                }
                else
                {
                    await _botClient.SendTextMessageAsync(_chat.ChatId, aiGeneratedTextResponse);
                    Console.WriteLine(contextForLogging);
                }
            }
            else
            {
                using var stream = await new GoogleTtsService().GetAudioAsync(aiGeneratedTextResponse);

                await typing.FinishTypingText(aiGeneratedTextResponse);
                await _botClient.SendVoiceAsync(_chat.ChatId, InputFile.FromStream(stream));
            }

            // This will also cause this method to trigger again. Handle this on this level.
            // Add this message to the global context ONLY after it has been received by telegram.
            _chat.AddMessage(new FoulMessage(Guid.NewGuid().ToString(), FoulMessageType.Bot, _botName, aiGeneratedTextResponse, DateTime.UtcNow, true));

            LogContextAndResponse(context, aiGeneratedTextResponse);
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
        finally
        {
            // Wait at least 3 seconds after each reply, but random up to 12.
            await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(3000, 12000)));

            _lock.Release();
            Console.WriteLine("We got to finally.");
        }
    }

    private void OnStatusChanged(object sender, FoulStatusChanged message) => OnStatusChangedAsync(message);
    public async Task OnStatusChangedAsync(FoulStatusChanged message)
    {
        if (message.WhoName != _botIdName)
            return;

        if (message.Status == ChatMemberStatus.Left
            || message.Status == ChatMemberStatus.Kicked)
        {
            _chat.MessageReceived -= OnMessageReceived;
            _subscribed = false;
            return;
        }

        // For any other status.
        if (!_subscribed)
            await JoinChatAsync(_chat, message.ByName);
    }

    private void LogContextAndResponse(List<FoulMessage> context, string aiGeneratedTextResponse)
    {
        Console.WriteLine();
        Console.WriteLine();

        foreach (var item in context)
        {
            Console.WriteLine(item.ToString());
        }

        Console.WriteLine(aiGeneratedTextResponse);
        Console.WriteLine();
        Console.WriteLine();
    }

    private List<FoulMessage> GetContextForAI(List<FoulMessage> fullContext)
    {
        var onlyAddressedToMe = fullContext
            .Where(message => ShouldAct(message) || IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_contextSize)
            .ToList();

        var allMessages = fullContext
            .Where(message => !ShouldAct(message) && !IsMyOwnMessage(message))
            .Select(message =>
            {
                if (!IsMyOwnMessage(message) && message.MessageType == FoulMessageType.Bot)
                    return message.AsUser();

                return message;
            })
            .TakeLast(_contextSize / 2) // Populate only second half with these messages.
            .ToList();

        var combinedContext = onlyAddressedToMe.Concat(allMessages)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Date)
            .TakeLast(_contextSize)
            .ToList();

        return new[] { new FoulMessage("Directive", FoulMessageType.System, "System", _directive, DateTime.MinValue, false) }
            .Concat(combinedContext)
            .ToList();
    }

    private bool IsMyOwnMessage(FoulMessage message)
    {
        return message.MessageType == FoulMessageType.Bot
            && message.SenderName == _botName;
    }

    private bool ShouldAct(FoulMessage message)
    {
        // TODO: Determine whether the bot should act (by key words, or by time, etc).
        if (message.SenderName == _botName)
            return false;

        if (message.ReplyTo == _botIdName)
            return true;

        Console.WriteLine($"{string.Join(',', _keyWords)} {message.Text}");
        if (_keyWords.Any(keyWord => message.Text.ToLowerInvariant().Contains(keyWord.ToLowerInvariant().Trim())))
            return true;

        return false;
    }
}
