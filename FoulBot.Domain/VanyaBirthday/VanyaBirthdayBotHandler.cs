using FoulBot.Domain.Connections;
using FoulBot.Domain.Features;

namespace FoulBot.Domain.VanyaBirthday;

public sealed class VanyaBirthdayBotHandlerFactory : IStandaloneBotHandlerFactory
{
    public IStandaloneBotHandler CreateStandaloneBotHandler(
        ChatScopedBotMessenger botMessenger,
        IFoulAIClient foulAIClient)
    {
        return new VanyaBirthdayBotHandler(foulAIClient, botMessenger);
    }
}

public sealed class VanyaBirthdayBotHandler : IStandaloneBotHandler
{
    private readonly IFoulAIClient _aiClient;
    private readonly ChatScopedBotMessenger _bot;

    public VanyaBirthdayBotHandler(
        IFoulAIClient aiClient,
        ChatScopedBotMessenger bot)
    {
        _aiClient = aiClient;
        _bot = bot;
    }

    public async ValueTask HandleMessage(FoulMessage message)
    {
        if (message.Text.ToLowerInvariant().Contains("hi"))
        {
            // Do something.
            await _bot.SendTextMessageAsync("Hi!");

            // Генерируем текст ИИ.
            var response = await _aiClient.GetCustomResponseAsync("Send cool message.");

            // Отправляем его в чат бот.
            await _bot.SendTextMessageAsync(response);

            // Генерируем голосовое сообщение.
            var audioResponse = await _aiClient.GetAudioResponseAsync("Some text.");

            // Отправляем его в чат бот как голосовое сообщение.
            await _bot.SendVoiceMessageAsync(audioResponse);
        }
    }
}
