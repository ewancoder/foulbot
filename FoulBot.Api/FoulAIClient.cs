using Azure.AI.OpenAI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FoulBot.Api;

public interface IFoulAIClient
{
    ValueTask<Tuple<string, string>> GetTextResponseAsync(string userName, string message);
    ValueTask<Stream> GetAudioResponseAsync(string userName, string message);
    ValueTask AddToContextAsync(string userName, string message);
    ValueTask<Tuple<string, string>> GetPersonalTextResponseAsync();
}

public sealed class FoulAIClient : IFoulAIClient
{
    private readonly int _maxMessagesInContext;
    private readonly OpenAIClient _client;
    private readonly List<ChatRequestMessage> _context = new List<ChatRequestMessage>();
    private readonly ChatRequestSystemMessage _systemMessage;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public FoulAIClient(
        string aiApiKey,
        int maxMessagesInContext,
        string mainDirective)
    {
        _maxMessagesInContext = maxMessagesInContext;
        _client = new OpenAIClient(aiApiKey);
        _systemMessage = new ChatRequestSystemMessage($"{mainDirective}");
    }

    public async ValueTask<Tuple<string, string>> GetTextResponseAsync(string userName, string message)
    {
        await _lock.WaitAsync();

        try
        {
            var messages = GetMessagesWithContext(userName, message);

            var options = new ChatCompletionsOptions("gpt-3.5-turbo", messages);
            var response = await _client.GetChatCompletionsAsync(options);
            var responseMessage = response.Value.Choices[0].Message;
            var content = responseMessage.Content;

            var cost = 100m * ((decimal)response.Value.Usage.PromptTokens) * 0.5m / 1_000_000m
                + 100m * ((decimal)response.Value.Usage.CompletionTokens) * 1.5m / 1_000_000m;
            var tokens = $"{response.Value.Usage.PromptTokens} + {response.Value.Usage.CompletionTokens} = {response.Value.Usage.TotalTokens}, {string.Format("{0:0.##}", cost)}";

            _context.Add(new ChatRequestAssistantMessage(content));

            return new(content, tokens);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask<Stream> GetAudioResponseAsync(string userName, string message)
    {
        var textResponse = await GetTextResponseAsync(userName, message);

        return await GenerateSpeechAsync(textResponse.Item1);
    }

    public async ValueTask AddToContextAsync(string userName, string message)
    {
        await _lock.WaitAsync();
        try
        {
            AddToContext(userName, message);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void AddToContext(string userName, string message)
    {
        _context.Add(new ChatRequestUserMessage(message)
        {
            Name = userName
        });
    }

    private void AddPersonalMessageToContext()
    {
        if (_context.Count == 0)
            _context.Add(new ChatRequestSystemMessage("Say that you've started working."));
        else
            _context.Add(new ChatRequestSystemMessage("Send your own message."));
    }

    private IEnumerable<ChatRequestMessage> GetMessagesWithContext()
    {
        // This is being called within a lock, so we can trim the list.
        while (_context.Count > _maxMessagesInContext)
            _context.RemoveAt(0);

        AddPersonalMessageToContext();

        return new[] { _systemMessage }.Concat(_context).ToList();
    }

    private IEnumerable<ChatRequestMessage> GetMessagesWithContext(string userName, string message)
    {
        // This is being called within a lock, so we can trim the list.
        while (_context.Count > _maxMessagesInContext)
            _context.RemoveAt(0);

        AddToContext(userName, message);

        return new[] { _systemMessage }.Concat(_context).ToList();
    }

    private async ValueTask<Stream> GenerateSpeechAsync(string message)
    {
        var response = await _client.GenerateSpeechFromTextAsync(new SpeechGenerationOptions
        {
            Voice = SpeechVoice.Onyx,
            Input = message,
            DeploymentName = "tts-1" // "tts-1-hd"
        });

        return response.Value.ToStream();
    }

    public async ValueTask<Tuple<string, string>> GetPersonalTextResponseAsync()
    {
        // TODO: This is duplicated code for now.
        await _lock.WaitAsync();

        try
        {
            var messages = GetMessagesWithContext();

            var options = new ChatCompletionsOptions("gpt-3.5-turbo", messages);
            var response = await _client.GetChatCompletionsAsync(options);
            var responseMessage = response.Value.Choices[0].Message;
            var content = responseMessage.Content;

            var cost = 100m * ((decimal)response.Value.Usage.PromptTokens) * 0.5m / 1_000_000m
                + 100m * ((decimal)response.Value.Usage.CompletionTokens) * 1.5m / 1_000_000m;
            var tokens = $"{response.Value.Usage.PromptTokens} + {response.Value.Usage.CompletionTokens} = {response.Value.Usage.TotalTokens}, {string.Format("{0:0.##}", cost)}";

            _context.Add(new ChatRequestAssistantMessage(content));

            return new(content, tokens);
        }
        finally
        {
            _lock.Release();
        }
    }
}
