using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using OpenAI.Audio;
using OpenAI.Chat;
using UnidecodeSharpCore;

namespace FoulBot.Infrastructure;

public sealed class FoulAIClientFactory : IFoulAIClientFactory
{
    private readonly ILogger<FoulAIClient> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly IConfiguration _configuration;

    public FoulAIClientFactory(
        ILogger<FoulAIClient> logger,
        ISharedRandomGenerator random,
        IConfiguration configuration)
    {
        _logger = logger;
        _random = random;
        _configuration = configuration;
    }

    public IFoulAIClient Create(string openAiModel)
        => new FoulAIClient(
            _logger,
            _random,
            _configuration,
            openAiModel);

}

public sealed partial class FoulAIClient : IFoulAIClient
{
    [GeneratedRegex(@"[^a-zA-Z_]", RegexOptions.Compiled, matchTimeoutMilliseconds: 50)]
    private static partial Regex NotAllowedCharacters();

    private readonly ILogger<FoulAIClient> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly ChatClient _client;
    private readonly AudioClient _audioClient;

    public FoulAIClient(
        ILogger<FoulAIClient> logger,
        ISharedRandomGenerator random,
        IConfiguration configuration,
        string openAiModel)
    {
        _logger = logger;
        _random = random;
        var key = configuration["OpenAIKey"]
            ?? throw new InvalidOperationException("Could not read OpenAIKey value.");

        _client = new ChatClient(model: openAiModel, key);
        _audioClient = new AudioClient("tts-1", key);
    }

    public async ValueTask<Stream> GetAudioResponseAsync(string text)
    {
        var response = await _audioClient.GenerateSpeechAsync(text, GeneratedSpeechVoice.Onyx);

        return response.Value.ToStream();
    }

    public ValueTask<string> GetCustomResponseAsync(string directive)
    {
        var aiContext = new[]
        {
            new SystemChatMessage(directive)
        };

        return GetTextResponseWithRetriesAsync(aiContext);
    }

    public async ValueTask<string> GetTextResponseAsync(IEnumerable<FoulMessage> context)
    {
        var aiContext = context.Select<FoulMessage, ChatMessage>(message =>
        {
            if (message.MessageType == FoulMessageType.System)
                return new SystemChatMessage(message.Text);

            if (message.MessageType == FoulMessageType.User)
                return new UserChatMessage(message.Text)
                {
                    ParticipantName = NotAllowedCharacters().Replace(message.SenderName.Unidecode(), string.Empty)
                };

            if (message.MessageType == FoulMessageType.Bot)
                return new AssistantChatMessage(message.Text)
                {
                    ParticipantName = NotAllowedCharacters().Replace(message.SenderName.Unidecode(), string.Empty)
                };

            throw new InvalidOperationException("Could not determine the type.");
        }).ToList();

        return await GetTextResponseWithRetriesAsync(aiContext);
    }

    private async ValueTask<string> GetTextResponseWithRetriesAsync(IEnumerable<ChatMessage> context)
    {
        var i = 0;
        while (i < 3)
        {
            i++;

            try
            {
                var response = await _client.CompleteChatAsync(context);
                if (response.Value.Content.Count == 0)
                    throw new InvalidOperationException("Could not get the response.");

                var text = response.Value.Content[0].Text;

                _logger.LogDebug(
                    "OpenAI tokens usage data: {@Context}, {ResponseMessage}, {PromptTokens}, {CompletionTokens}, {TotalTokens}",
                    context,
                    text,
                    response.Value.Usage.InputTokens,
                    response.Value.Usage.OutputTokens,
                    response.Value.Usage.TotalTokens);

                return text;
            }
            catch (Exception exception) // TODO: Figure out what exception the new library returns.
            {
                var delay = _random.Generate(500, 1800);
                _logger.LogWarning(exception, "OpenAI returned exception. Retrying up to 3 times with a delay {Delay} ms.", delay);
                await Task.Delay(delay);

                if (i == 3)
                {
                    _logger.LogError(exception, "OpenAI returned exception 3 times in a row. Cannot get a response.");
                    throw;
                }
            }
        }

        throw new InvalidOperationException("Logic never comes here.");
    }
}
