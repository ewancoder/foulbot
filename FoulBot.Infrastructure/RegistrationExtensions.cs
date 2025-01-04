using System.Reflection;
using FoulBot.Domain.Connections;
using FoulBot.Domain.Storage;
using FoulBot.Infrastructure.Discord;
using FoulBot.Infrastructure.Storage;
using FoulBot.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

namespace FoulBot.Infrastructure;

public static class Constants
{
    public static class BotTypes
    {
        public const string Telegram = "telegram";
        public const string Discord = "discord";
    }
}

public sealed record FoulBotServerBuilder(
    IServiceCollection Services,
    IConfiguration Configuration)
{
    public static FoulBotServerBuilder Create(bool isDebug, bool isInMemory)
    {
        var services = new ServiceCollection();

        var configuration = services.AddConfiguration(isDebug: isDebug);

        services
            .AddLogging(configuration, isDebug)
            .AddFoulBotDomain()
            .AddFoulBotInfrastructure(configuration, isDebug, isInMemory);

        return new(services, configuration);
    }

    public ServiceProvider BuildServiceProvider()
        => Services.BuildServiceProvider();
}

public static class RegistrationExtensions
{
    public static IServiceCollection AddFoulBotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDebug, bool isInMemory)
    {
        _ = isDebug;
        services
            .AddCachedReminderStore<NonThreadSafeFileReminderStorage>()
            .AddChatPool<TelegramDuplicateMessageHandler>(Constants.BotTypes.Telegram)
            .AddChatPool<DiscordDuplicateMessageHandler>(Constants.BotTypes.Discord)
            .AddSingleton<IAllowedChatsProvider, AllowedChatsProvider>()
            .AddSingleton<IVectorStoreMapping, InMemoryVectorStoreMapping>()
            .AddTransient<IFoulAIClientFactory, FoulAIClientFactory>()                 // OpenAI
            .AddTransient<IGoogleTtsService, GoogleTtsService>()                       // Google
            .AddTransient<ITelegramBotMessengerFactory, TelegramBotMessengerFactory>() // Telegram
            .AddTransient<IFoulMessageFactory, FoulMessageFactory>()
            .AddTransient<ITelegramUpdateHandlerFactory, TelegramUpdateHandlerFactory>()
            .AddKeyedTransient<IBotConnectionHandler, TelegramBotConnectionHandler>(Constants.BotTypes.Telegram)
            .AddTransient<IDiscordBotMessengerFactory, DiscordBotMessengerFactory>()   // Discord
            .AddKeyedTransient<IBotConnectionHandler>(Constants.BotTypes.Discord, (p, _) => new DiscordBotConnectionHandler(
                p.GetRequiredService<ILogger<DiscordBotConnectionHandler>>(),
                p.GetRequiredService<IDiscordBotMessengerFactory>(),
                p.GetRequiredService<IFoulBotFactory>(),
                p.GetRequiredService<IAllowedChatsProvider>(),
                p.GetRequiredKeyedService<ChatPool>(Constants.BotTypes.Discord)));

        if (!isInMemory)
        {
            var redisConnectionString = configuration["RedisConnectionString"]
                ?? throw new InvalidOperationException("Could not get Redis connection string.");

            // Register as factory so that the DI dispose of it.
            services
                .AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString)) // Necessary evil: Sync connect on startup.
                .AddSingleton<IContextStore, RedisContextStore>()
                .AddSingleton<IVectorStoreMapping, RedisVectorStoreMapping>();
        }

        return services;
    }

    public static IServiceCollection AddCachedReminderStore<TReminderStore>(
        this IServiceCollection services)
        where TReminderStore : class, IReminderStore
    {
        return services.AddTransient<IReminderStore, TReminderStore>()
            .Decorate<IReminderStore, InMemoryLockingReminderStoreDecorator>();
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
            .Destructure.ByTransforming<Attachment>(a => new { a.Name })
            .CreateLogger();

        logger.Write(LogEventLevel.Information, "hi");

        return services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
    }
}

