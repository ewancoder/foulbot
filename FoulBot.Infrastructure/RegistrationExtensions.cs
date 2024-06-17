using FoulBot.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.Infrastructure;

public static class RegistrationExtensions
{
    public static IServiceCollection AddFoulBotInfrastructure(this IServiceCollection services)
    {
        return services
            .AddTransient<IFoulAIClient, FoulAIClient>()
            .AddTransient<IGoogleTtsService, GoogleTtsService>()
            .AddTransient<ITelegramBotMessengerFactory, TelegramBotMessengerFactory>()
            .AddSingleton<IBotDelayStrategy, BotDelayStrategy>();
    }
}
