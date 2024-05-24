using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnidecodeSharpFork;

namespace FoulBot.Api;

public sealed class FoulAIClient : IFoulAIClient
{
    private readonly OpenAIClient _client;

    public FoulAIClient(IConfiguration configuration)
    {
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
        var response = await _client.GetChatCompletionsAsync(options);
        var responseMessage = response.Value.Choices[0].Message;
        var content = responseMessage.Content;

        return content;
    }
}
