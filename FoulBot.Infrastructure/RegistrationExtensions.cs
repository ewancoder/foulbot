using FoulBot.Domain;
using FoulBot.Infrastructure.Telegram;
using Microsoft.Extensions.DependencyInjection;

namespace FoulBot.Infrastructure;

public static class RegistrationExtensions
{
    public static IServiceCollection AddFoulBotInfrastructure(this IServiceCollection services)
    {
        return services
            .AddSingleton<IAllowedChatsProvider, AllowedChatsProvider>()    // Domain
            .AddSingleton<IBotDelayStrategy, BotDelayStrategy>()
            .AddTransient<IFoulBotFactory, FoulBotFactory>()
            .AddTransient<IFoulChatFactory, FoulChatFactory>()
            .AddScoped<ChatPool>()
            .AddTransient<IFoulAIClientFactory, FoulAIClientFactory>()      // OpenAI
            .AddTransient<IGoogleTtsService, GoogleTtsService>()            // Google
            .AddTransient<ITelegramBotMessengerFactory, TelegramBotMessengerFactory>() // Telegram
            .AddTransient<IFoulMessageFactory, FoulMessageFactory>()
            .AddTransient<ITelegramUpdateHandlerFactory, TelegramUpdateHandlerFactory>();
    }
}
