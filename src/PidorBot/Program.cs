using Microsoft.Extensions.Hosting;
using PidorBot;
using PidorBot.Chats;
using Serilog;
using Serilog.Events;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

Settings settings;
try
{
    settings = Settings.Load(builder.Configuration, builder.Environment);
}
catch (Exception exception) when (exception is InvalidOperationException or TimeZoneNotFoundException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("PidorBot", LogEventLevel.Debug)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "PidorBot")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

if (settings.SeqUrl != null)
    logger = logger.WriteTo.Seq(settings.SeqUrl, apiKey: settings.SeqApiKey);

Log.Logger = logger.CreateLogger();

try
{
    var telegram = new TelegramBotClient(settings.TelegramBotToken);
    var me = await telegram.GetMe();
    var bot = new BotIdentity(me.Id, me.Username ?? throw new InvalidOperationException("Bot has no username."));
    Log.Information("Starting as @{Username}, chat model {ChatModel}, thinking model {ThinkingModel}",
        bot.Username, settings.ChatModel, settings.ThinkingModel);

    builder.Services.AddSerilog().AddPidorBot(settings, bot, telegram);

    await builder.Build().RunAsync();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "PidorBot crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
