using System.Text;
using FoulBot.Domain.Connections;

namespace FoulBot.Domain.Features;

public sealed class AdvertisementFeature : BotFeature
{
    private readonly IFoulBot _bot;
    private readonly string _botId;
    private readonly List<FoulBotConfiguration> _allBots;
    private readonly IFoulAIClient _aiClient;

    public AdvertisementFeature(
        IFoulBot bot,
        string botId,
        IEnumerable<BotConnectionConfiguration> allBots,
        IFoulAIClient aiClient)
    {
        _bot = bot;
        _botId = botId;
        _allBots = allBots
            .Select(x => x.Configuration)
            .Where(x => x.IsPublic)
            .ToList();
        _aiClient = aiClient;
    }

    public override async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        var text = CutKeyword(message.Text, $"@{_botId}");
        if (text == null)
            return false;

        if (text != "advertise bots")
            return false;

        var advertisement = await GetBotsAdvertisements();

        await _bot.SendRawAsync(advertisement);

        return true;
    }

    public override ValueTask StopFeatureAsync()
    {
        return default;
    }

    private async ValueTask<string> GetBotsAdvertisements()
    {
        var sb = new StringBuilder();

        foreach (var bot in _allBots.OrderBy(x => x.BotId))
        {
            var ad = await _aiClient.GetCustomResponseAsync($"{bot.Directive} You are a bot. Advertise yourself in a very short and terse summary.");
            sb.AppendLine($"@{bot.BotId} - {ad}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
