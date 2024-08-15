using System.Reflection;
using FoulBot.Api;
using FoulBot.Domain;
using FoulBot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace FoulBot.App;

public sealed class FoulBotServer
{
    public static async Task StartAsync(CancellationToken cancellationToken)
    {
        var isDebug = false;
#if DEBUG
        isDebug = true;
#endif

        var services = new ServiceCollection();
        var configuration = services.AddConfiguration(isDebug: isDebug);

        services
            .AddLogging(configuration, isDebug)
            .AddTransient<IFoulMessageFactory, FoulMessageFactory>()
            .AddTransient<ITelegramUpdateHandlerFactory, TelegramUpdateHandlerFactory>()
            .AddTransient<IFoulBotFactory, FoulBotFactory>()
            .AddTransient<IFoulChatFactory, FoulChatFactory>()
            .AddScoped<ChatLoader>()
            .AddScoped<IChatCache>(x => x.GetRequiredService<ChatLoader>())
            .AddScoped<ChatPool>()
            .AddScoped<ApplicationInitializer>()
            .AddFoulBotInfrastructure()
            .RegisterBots(isDebug);

        {
            using var rootProvider = services.BuildServiceProvider();
            await using var scope = rootProvider.CreateAsyncScope(); // We need this, root container doesn't work with IAsyncDisposable.
            var provider = scope.ServiceProvider;
            using var localCts = new CancellationTokenSource();
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, localCts.Token);

            var logger = provider.GetRequiredService<ILogger<FoulBotServer>>();

            var appInitializer = provider.GetRequiredService<ApplicationInitializer>();
            try
            {
                appInitializer.Initialize(combinedCts.Token);

                logger.LogInformation("Application started.");
                await Task.Delay(Timeout.Infinite, combinedCts.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Graceful shutdown initiated.");
                await appInitializer.GracefullyShutdownAsync();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Error happened during initialization.");
                throw;
            }
            finally
            {
                logger.LogInformation("Exiting...");

                // Provider is not disposed yet. We can afford graceful shutdown.
                await localCts.CancelAsync(); // Cancels combinedCts.Token.

                logger.LogInformation("Application stopped.");
            }
        }

        // Provider and CTSs are disposed here.
    }
}

public static class FoulBotServerExtensions
{
    public static IConfiguration AddConfiguration(
        this IServiceCollection services, bool isDebug)
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddEnvironmentVariables();

        if (isDebug)
            configurationBuilder.AddUserSecrets(Assembly.GetExecutingAssembly());

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
