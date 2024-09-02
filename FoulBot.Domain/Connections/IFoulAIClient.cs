namespace FoulBot.Domain.Connections;

public interface IFoulAIClientFactory
{
    IFoulAIClient Create(string openAiModel);
}

public interface IFoulAIClient
{
    ValueTask<string> GetTextResponseAsync(IEnumerable<FoulMessage> context);
    ValueTask<string> GetCustomResponseAsync(string directive);
    ValueTask<Stream> GetAudioResponseAsync(string text);
}
