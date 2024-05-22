using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace FoulBot.Api;

public sealed class TypingImitator : IDisposable
{
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly ITelegramBotClient _client;
    private readonly ChatId _chat;
    private readonly Task _typing;
    private readonly DateTime _startedAt;
    private readonly ChatAction _action;
    private string? _text = null;

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

        while (_text == null)
        {
            if (DateTime.UtcNow - _startedAt > TimeSpan.FromMinutes(1))
                return;

            await _client.SendChatActionAsync(_chat, _action);
            try
            {
                await Task.Delay(random.Next(300, 10000), _cts.Token);
            }
            catch
            {
            }
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
        catch
        {
        }
    }
}
