using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using UnidecodeSharpCore;

namespace FoulBot.Infrastructure;

public sealed class FoulAIClientFactory : IFoulAIClientFactory
{
    private readonly ILogger<FoulAIClient> _logger;
    private readonly IConfiguration _configuration;

    public FoulAIClientFactory(
        ILogger<FoulAIClient> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public IFoulAIClient Create(
        IContextPreserverClient contextPreserverClient,
        string openAiModel)
    {
        return new ContextPreservingFoulAIClient(
            contextPreserverClient,
            new FoulAIClient(_logger, _configuration, openAiModel));
    }
}

public sealed class FoulAIClient : IFoulAIClient
{
    private readonly Random _random = new Random();
    private readonly ILogger<FoulAIClient> _logger;
    private readonly OpenAIClient _client;
    private readonly string _openAiModel;

    public FoulAIClient(
        ILogger<FoulAIClient> logger,
        IConfiguration configuration,
        string openAiModel)
    {
        _logger = logger;
        _client = new OpenAIClient(configuration["OpenAIKey"]);
        _openAiModel = openAiModel;
    }

    public async ValueTask<Stream> GetAudioResponseAsync(string text)
    {
        var response = await _client.GenerateSpeechFromTextAsync(new SpeechGenerationOptions
        {
            Voice = SpeechVoice.Onyx,
            Input = text,
            DeploymentName = "tts-1"
        });

        return response.Value.ToStream();
    }

    public async ValueTask<string> GetTextResponseAsync(IEnumerable<FoulMessage> context)
    {
        var aiContext = context.Select<FoulMessage, ChatRequestMessage>(message =>
        {
            if (message.MessageType == FoulMessageType.System)
                return new ChatRequestSystemMessage(message.Text);

            if (message.MessageType == FoulMessageType.User)
                return new ChatRequestUserMessage(message.Text)
                {
                    Name = message.SenderName.Unidecode()
                };

            if (message.MessageType == FoulMessageType.Bot)
                return new ChatRequestAssistantMessage(message.Text)
                {
                    Name = message.SenderName.Unidecode()
                };

            throw new InvalidOperationException("Could not determine the type.");
        }).ToList();

        var options = new ChatCompletionsOptions(_openAiModel, aiContext);

        return await GetTextResponseWithRetriesAsync(options);
    }

    public async ValueTask<string> GetCustomResponseAsync(string directive)
    {
        var aiContext = new[]
        {
            new ChatRequestSystemMessage(directive)
        };

        var options = new ChatCompletionsOptions(_openAiModel, aiContext);

        return await GetTextResponseWithRetriesAsync(options);
    }

    private async ValueTask<string> GetTextResponseWithRetriesAsync(ChatCompletionsOptions options)
    {
        var i = 0;
        while (i < 3)
        {
            i++;

            try
            {
                var response = await _client.GetChatCompletionsAsync(options);
                var responseMessage = response.Value.Choices[0].Message;
                var content = responseMessage.Content;

                _logger.LogDebug(
                    "OpenAI tokens usage data: {@Options}, {ResponseMessage}, {PromptTokens}, {CompletionTokens}, {TotalTokens}",
                    options,
                    content,
                    response.Value.Usage.PromptTokens,
                    response.Value.Usage.CompletionTokens,
                    response.Value.Usage.TotalTokens);

                return content;
            }
            catch (RequestFailedException exception)
            {
                var delay = _random.Next(500, 1800);
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
