global using FoulBot.Domain;
global using FoulBot.Infrastructure;

using FoulBot.App;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.CancelAsync();
};

await FoulBotServer.StartAsync(cts.Token);
