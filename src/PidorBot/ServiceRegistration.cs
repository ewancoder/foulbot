using Microsoft.Extensions.DependencyInjection;
using PidorBot.AI;
using PidorBot.Brain;
using PidorBot.Chats;
using PidorBot.Reminders;
using PidorBot.Storage;
using PidorBot.Telegram;
using StackExchange.Redis;
using Telegram.Bot;

namespace PidorBot;

public static class ServiceRegistration
{
    public static IServiceCollection AddPidorBot(
        this IServiceCollection services, Settings settings, BotIdentity bot, ITelegramBotClient telegram)
    {
        var redisOptions = ConfigurationOptions.Parse(settings.RedisConnectionString);
        redisOptions.AbortOnConnectFail = false; // Keep reconnecting, the bot works without Redis (forgetfully).

        return services
            .AddSingleton(settings)
            .AddSingleton(bot)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(Random.Shared)
            .AddSingleton(telegram)
            .AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions))
            .AddSingleton<IChatStore, RedisChatStore>()
            .AddSingleton<INameStore, RedisNameStore>()
            .AddSingleton<IStickerBook, StickerBook>()
            .AddSingleton(provider => new AccessList(
                provider.GetRequiredService<ILogger<AccessList>>(), Path.Combine(settings.DataDirectory, "allowed_chats"), bot.Username))
            .AddSingleton(provider => new ReminderBook(
                provider.GetRequiredService<ILogger<ReminderBook>>(), Path.Combine(settings.DataDirectory, "reminders"), bot.Username))
            .AddSingleton<ILlm, Llm>()
            .AddSingleton<IVoice, OpenAIVoice>()
            .AddSingleton<IImageGenerator, ImageGenerator>()
            .AddSingleton<IMessenger, TelegramMessenger>()
            .AddSingleton<IPacing, HumanPacing>()
            .AddSingleton<Responder>()
            .AddSingleton<MemoryKeeper>()
            .AddSingleton<MoodKeeper>()
            .AddSingleton<ChatServices>()
            .AddSingleton<ChatHub>()
            .AddHostedService<TelegramPoller>()
            .AddHostedService<HeartbeatService>()
            .AddHostedService<ReminderService>();
    }
}
