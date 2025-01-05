
using FoulBot.Domain.Connections;
using HtmlAgilityPack;

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
        var now = DateTime.UtcNow;
        var client = new HttpClient();

        var response = await client.GetAsync($"https://nationaltoday.com/{now.ToString("MMMM").ToLowerInvariant()}-{now.Day}-holidays/");
        var content = await response.Content.ReadAsStringAsync();

        var doc = new HtmlDocument();
        doc.LoadHtml(content);
        var celebrations = doc.DocumentNode.SelectNodes("//*[contains(@class, 'holiday-title')]")
            .Select(x => x.InnerText.Trim())
            .ToList();

        var todayIs = await _foulAiClientFactory.Create("gpt-4o-mini")
            .GetCustomResponseAsync($"Today {DateTime.UtcNow.Day} of {DateTime.UtcNow.ToString("MMM")} the following things are celebrated: {string.Join(", ", celebrations)}. Pick one or couple most notable and write a short description. {(_language == null ? string.Empty : $"Use {_language} language.")}.");

        await _bot.PerformRequestAsync(ChatParticipant.System, $"Congratulate everyone, describing these holidays. {(_language == null ? string.Empty : $"in {_language} language")}in {_language}! Today is {todayIs}");
    }
}
