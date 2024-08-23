using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
            .AddSingleton(TimeProvider.System)
            .AddScoped<ISharedRandomGenerator, SharedRandomGenerator>()
            .AddScoped<ChatLoader>()
            .AddScoped<ApplicationInitializer>()
            .AddFoulBotInfrastructure()
            .RegisterBots(configuration, isDebug);

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
                await appInitializer.InitializeAsync(combinedCts.Token);

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
