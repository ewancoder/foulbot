using Azure;
using Azure.AI.OpenAI;
using FoulBot.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnidecodeSharpCore;

namespace FoulBot.Infrastructure;

public sealed class FoulAIClient : IFoulAIClient
{
    private readonly Random _random = new Random();
    private readonly ILogger<FoulAIClient> _logger;
    private readonly OpenAIClient _client;

    public FoulAIClient(
        ILogger<FoulAIClient> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _client = new OpenAIClient(configuration["OpenAIKey"]);
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

        var options = new ChatCompletionsOptions("gpt-3.5-turbo", aiContext);
        var response = await _client.GetChatCompletionsAsync(options);
        var responseMessage = response.Value.Choices[0].Message;
        var content = responseMessage.Content;

        return content;
    }

    public async ValueTask<string> GetCustomResponseAsync(string directive)
    {
        var aiContext = new[]
        {
            new ChatRequestSystemMessage(directive)
        };

        var options = new ChatCompletionsOptions("gpt-3.5-turbo", aiContext);

        return await GetCustomResponseWithRetriesAsync(options);
    }

    private async ValueTask<string> GetCustomResponseWithRetriesAsync(ChatCompletionsOptions options)
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
