using FoulBot.Domain.Connections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.App;

public static class BotsRegistrationExtensions
{
    public static IServiceCollection RegisterBot(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationKeyForApiKey,
        FoulBotConfiguration botConfiguration,
        string type = Constants.BotTypes.Telegram)
    {
        var connectionString = configuration[configurationKeyForApiKey]
            ?? throw new InvalidOperationException($"Connection string for {botConfiguration.BotId} bot is missing.");

        return services.AddSingleton(new BotConnectionConfiguration(
            type, connectionString, botConfiguration));
    }
}
