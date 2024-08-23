using System.Reflection;
using FoulBot.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace FoulBot.Infrastructure;

public static class Constants
{
    public static class BotTypes
    {
        public const string Telegram = "telegram";
        public const string Discord = "discord";
    }
}

public static class RegistrationExtensions
{
    public static IServiceCollection AddFoulBotInfrastructure(this IServiceCollection services)
    {
        return services
            .AddScoped<IAllowedChatsProvider, AllowedChatsProvider>()    // Domain
            .AddScoped<IBotDelayStrategy, BotDelayStrategy>()
            .AddChatPool<TelegramDuplicateMessageHandler>("Telegram")
            .AddTransient<IFoulBotFactory, FoulBotFactory>()
            .AddTransient<IFoulChatFactory, FoulChatFactory>()
            .AddTransient<IFoulAIClientFactory, FoulAIClientFactory>()      // OpenAI
            .AddTransient<IGoogleTtsService, GoogleTtsService>()            // Google
            .AddTransient<ITelegramBotMessengerFactory, TelegramBotMessengerFactory>() // Telegram
            .AddTransient<IFoulMessageFactory, FoulMessageFactory>()
            .AddTransient<ITelegramUpdateHandlerFactory, TelegramUpdateHandlerFactory>()
            .AddKeyedTransient<IBotConnectionHandler, TelegramBotConnectionHandler>(Constants.BotTypes.Telegram);
    }

    public static IServiceCollection AddChatPool<TDuplicateMessageHandler>(
        this IServiceCollection services, string key)
        where TDuplicateMessageHandler : class, IDuplicateMessageHandler
    {
        return services
            .AddTransient<TDuplicateMessageHandler>()
            .AddKeyedScoped(key, (provider, _) => new ChatPool(
                provider.GetRequiredService<ILogger<ChatPool>>(),
                provider.GetRequiredService<IFoulChatFactory>(),
                provider.GetRequiredService<TDuplicateMessageHandler>(),
                provider.GetRequiredService<IAllowedChatsProvider>())); // TODO: Rewrite into ChatPool factory.
    }

    public static IConfiguration AddConfiguration(
        this IServiceCollection services, bool isDebug)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        if (isDebug)
            configurationBuilder.AddUserSecrets(Assembly.GetEntryAssembly()!);

        var configuration = configurationBuilder.Build();

        services.AddSingleton<IConfiguration>(configuration);

        return configuration;
    }

    public static IServiceCollection AddLogging(
        this IServiceCollection services, IConfiguration configuration, bool isDebug)
    {
        var loggerBuilder = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("FoulBot", LogEventLevel.Verbose);

        loggerBuilder = isDebug
            ? loggerBuilder.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}\n{Properties}\n{NewLine}{Exception}")
            : loggerBuilder.WriteTo.Seq(
                "http://31.146.143.167:5341", apiKey: configuration["SeqApiKey"]);
        // TODO: Move IP address to configuration.

        var logger = loggerBuilder
            .Enrich.WithThreadId()
            .CreateLogger();

        logger.Write(LogEventLevel.Information, "hi");

        return services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
    }
}
