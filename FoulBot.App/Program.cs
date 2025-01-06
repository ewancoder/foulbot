global using FoulBot.Domain;
global using FoulBot.Infrastructure;
using FoulBot.App;

using var cts = new CancellationTokenSource();

AppDomain.CurrentDomain.ProcessExit += (_, e)
    => GracefullyShutdown().GetAwaiter().GetResult();

Console.CancelKeyPress += (_, e) =>
{
    GracefullyShutdown().GetAwaiter().GetResult();
    e.Cancel = true;
};

async Task GracefullyShutdown()
{
    Console.WriteLine("Gracefully shutting down.");

    await cts.CancelAsync();

    // Wait an arbitrary amount of time hoping our cleanup logic will be done by then.
    await Task.Delay(TimeSpan.FromSeconds(5));

    Console.WriteLine("Gracefully shut down the application.");
}

await FoulBotServer.StartAsync(cts.Token);
