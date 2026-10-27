using System.Globalization;
using System.Text;
using PidorBot.Brain;

namespace PidorBot.Chats;

/// <summary>
/// $commands for the people who know about them. Everything else the bot does by itself.
/// $activate and $deactivate are handled by <see cref="ChatHub"/>.
/// </summary>
public sealed partial class ChatSession
{
    private async Task<bool> TryHandleCommandAsync(ChatMessage message)
    {
        var text = message.Text.Trim();
        if (!text.StartsWith('$'))
            return false;

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var argument = parts.Length > 1 ? parts[1] : null;

        switch (parts[0].ToLowerInvariant())
        {
            // The old command: toggles the hardcore mode.
            case "$pidormode":
                await SetModeAsync(_mode == Modes.Hardcore ? Modes.Default : Modes.Hardcore);
                return true;

            case "$mode":
                var mode = Modes.Find(argument);
                if (mode == null)
                    await SendRawAsync(DescribeModes());
                else
                    await SetModeAsync(mode);

                return true;

            case "$status":
                await SendRawAsync(await DescribeStatusAsync());
                return true;

            case "$memory":
                var memory = await _services.Memory.GetAsync(Chat);
                await SendRawAsync(memory?.Text ?? "Пока ничего не помню.");
                return true;

            case "$forget":
                await _services.Memory.ForgetAsync(Chat);
                await SendRawAsync("Всё забыл.");
                return true;

            // Thinking takes a while, don't hold up other messages.
            case "$newmood":
                RunInBackground("think of a new mood", async () =>
                {
                    var mood = await _services.Moods.RenewAsync(Chat, _mode, GetHistory(), Stopping);
                    await SendRawAsync($"Новое настроение: {mood}");
                });
                return true;

            default:
                return false;
        }
    }

    private async Task SetModeAsync(BotMode mode)
    {
        lock (_lock)
        {
            _mode = mode;

            // Otherwise the bot keeps imitating its own messages from the previous mode.
            _contextStartsAt = Now;
        }

        await _services.Store.SetModeAsync(Chat, mode.Name);
        await SendRawAsync($"{mode.Emoji} Режим: {mode.Name} ({mode.Description})");
        _logger.LogWarning("Switched mode in chat {ChatId} to {Mode}", Chat, mode.Name);
    }

    private string DescribeModes()
    {
        var sb = new StringBuilder($"Сейчас: {_mode.Emoji} {_mode.Name}\n\n");
        foreach (var mode in Modes.All)
            sb.Append(CultureInfo.InvariantCulture, $"{mode.Emoji} $mode {mode.Name} — {mode.Description}\n");

        return sb.ToString();
    }

    private async Task<string> DescribeStatusAsync()
    {
        var mood = await _services.Moods.PeekAsync(Chat);
        var memory = await _services.Memory.GetAsync(Chat);
        var timeZone = _services.Settings.TimeZone;

        string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);

        return $"""
            Режим: {_mode.Emoji} {_mode.Name}
            Настроение: {(mood == null ? "ещё не выбрано" : $"{mood.Text} (до {Local(mood.ExpiresAt)})")}
            Память: {(memory == null ? "пусто" : $"обновлена {Local(memory.UpdatedAt)}, {memory.Text.Length} символов")}
            Напоминаний: {(await _services.Reminders.GetAsync(Chat)).Count}
            """;
    }

    private Task SendRawAsync(string text) => _services.Messenger.SendTextAsync(Chat, text, Stopping);
}
