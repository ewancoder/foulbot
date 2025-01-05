
using FoulBot.Domain.Connections;

namespace FoulBot.Domain.Features;

public sealed class DailyCelebrationFeature : IBotFeature
{
    // TODO: Implement stopping the feature: cancel token source.
    private readonly IFoulAIClientFactory _foulAiClientFactory;
    private readonly IFoulBot _bot;
    private readonly string? _language;
    private readonly Dictionary<string, bool> _celebrated = new();

    public DailyCelebrationFeature(
        IFoulAIClientFactory foulAiClientFactory,
        IFoulBot bot,
        string? language)
    {
        _foulAiClientFactory = foulAiClientFactory;
        _bot = bot;
        _language = language;

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));

                var key = $"{DateTime.UtcNow.Day}-{DateTime.UtcNow.Month}";
                if (_celebrated.ContainsKey(key) || DateTime.UtcNow.Hour < 12)
                    continue;

                _celebrated.Add(key, true);

                await SayWhatIsTodayAsync();
            }
        });
    }

    public async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        if (message.Text.ToLowerInvariant().Trim('?').Trim() == "what day is today")
        {
            await SayWhatIsTodayAsync();
            return true;
        }

        return false;
    }

    public ValueTask StopFeatureAsync()
    {
        return default;
    }

    private async ValueTask SayWhatIsTodayAsync()
    {
        var todayIs = await _foulAiClientFactory.Create("gpt-4o-mini")
            .GetCustomResponseAsync($"What is celebrated worldwide on {DateTime.UtcNow.Day} of {DateTime.UtcNow.ToString("MMM")}? Return the name of the celebration, followed by a short summary. {(_language == null ? string.Empty : $"Use {_language} language.")}.");

        await _bot.PerformRequestAsync(ChatParticipant.System, $"Congratulate everyone {(_language == null ? string.Empty : $"in {_language} language")}in {_language}! Today is {todayIs}");
    }
}
