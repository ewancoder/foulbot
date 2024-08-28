using System.ClientModel;
using System.Text.RegularExpressions;
using FoulBot.Domain.Features;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Assistants;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Files;
using OpenAI.VectorStores;
using StackExchange.Redis;
using UnidecodeSharpCore;

namespace FoulBot.Infrastructure;

public interface IVectorStoreMapping
{
    ValueTask<string?> GetVectorStoreIdAsync(string storeName);
    ValueTask CreateMappingAsync(string storeName, string vectorStoreId);
    ValueTask ClearAsync(string storeName);
}

public sealed class InMemoryVectorStoreMapping : IVectorStoreMapping
{
    private readonly Dictionary<string, string> _storeNameToVectorStoreId = new();

    public ValueTask ClearAsync(string storeName)
    {
        _storeNameToVectorStoreId.Remove(storeName);
        return default;
    }

    public ValueTask CreateMappingAsync(string storeName, string vectorStoreId)
    {
        _storeNameToVectorStoreId[storeName] = vectorStoreId;
        return default;
    }

    public ValueTask<string?> GetVectorStoreIdAsync(string storeName)
    {
        if (_storeNameToVectorStoreId.TryGetValue(storeName, out var vectorStoreId))
            return new(vectorStoreId);

        return new((string?)null);
    }
}

public sealed class RedisVectorStoreMapping : IVectorStoreMapping
{
    private readonly ILogger<RedisVectorStoreMapping> _logger;
    private readonly IConnectionMultiplexer _redis;

    public RedisVectorStoreMapping(
        ILogger<RedisVectorStoreMapping> logger,
        IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    public async ValueTask ClearAsync(string storeName)
    {
        var db = _redis.GetDatabase();

        await db.KeyDeleteAsync(GetKey(storeName));
    }

    public async ValueTask CreateMappingAsync(string storeName, string vectorStoreId)
    {
        var db = _redis.GetDatabase();

        await db.StringSetAsync(GetKey(storeName), vectorStoreId);
    }

    public async ValueTask<string?> GetVectorStoreIdAsync(string storeName)
    {
        var db = _redis.GetDatabase();

        // This issue doesn't reproduce locally, but for some reason it does on the server.
        try
        {
            var value = await db.StringGetAsync(GetKey(storeName));
            return value;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error happened when trying to get vector store mapping. Returning null and creating a new vector store.");
            return null;
        }
    }

    private static string GetKey(string storeName)
        => $"vector__{storeName}";
}

public sealed class FoulAIClientFactory : IFoulAIClientFactory
{
    private readonly ILogger<FoulAIClient> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly IConfiguration _configuration;
    private readonly IVectorStoreMapping _vectorStoreMapping;

    public FoulAIClientFactory(
        ILogger<FoulAIClient> logger,
        ISharedRandomGenerator random,
        IConfiguration configuration,
        IVectorStoreMapping vectorStoreMapping)
    {
        _logger = logger;
        _random = random;
        _configuration = configuration;
        _vectorStoreMapping = vectorStoreMapping;
    }

    public IFoulAIClient Create(string openAiModel)
        => new FoulAIClient(
            _logger,
            _random,
            _vectorStoreMapping,
            _configuration,
            openAiModel);

    public IDocumentSearch CreateDocumentSearch(string openAiModel)
        => new FoulAIClient(
            _logger,
            _random,
            _vectorStoreMapping,
            _configuration,
            openAiModel);
}

public sealed partial class FoulAIClient : IFoulAIClient, IDocumentSearch
{
    [GeneratedRegex(@"[^a-zA-Z_]", RegexOptions.Compiled, matchTimeoutMilliseconds: 50)]
    private static partial Regex NotAllowedCharacters();

    private readonly ILogger<FoulAIClient> _logger;
    private readonly ISharedRandomGenerator _random;
    private readonly IVectorStoreMapping _vectorStoreMapping;
    private readonly ChatClient _client;
    private readonly AudioClient _audioClient;

#pragma warning disable OPENAI001
    private readonly FileClient _fileClient;
    private readonly AssistantClient _assistantClient;
    private readonly VectorStoreClient _vectorClient;

    public FoulAIClient(
        ILogger<FoulAIClient> logger,
        ISharedRandomGenerator random,
        IVectorStoreMapping vectorStoreMapping,
        IConfiguration configuration,
        string openAiModel)
    {
        _logger = logger;
        _random = random;
        _vectorStoreMapping = vectorStoreMapping;
        var key = configuration["OpenAIKey"]
            ?? throw new InvalidOperationException("Could not read OpenAIKey value.");

        _client = new(model: openAiModel, key);
        _audioClient = new("tts-1", key);
        _fileClient = new(key);
        _assistantClient = new(key);
        _vectorClient = new(key);
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
            if (message.SenderType == FoulMessageSenderType.System)
                return new SystemChatMessage(message.Text);

            if (message.SenderType == FoulMessageSenderType.User)
                return new UserChatMessage(message.Text)
                {
                    ParticipantName = NotAllowedCharacters().Replace(message.SenderName.Unidecode(), string.Empty)
                };

            if (message.SenderType == FoulMessageSenderType.Bot)
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

                LogTokensUsage(context, text, response.Value.Usage);

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

    private void LogTokensUsage(
        IEnumerable<ChatMessage> context, string text, ChatTokenUsage usage)
    {
        _logger.LogDebug(
                "OpenAI tokens usage data: {@Context}, {ResponseMessage}, {PromptTokens}, {CompletionTokens}, {TotalTokens}",
                context,
                text,
                usage.InputTokens,
                usage.OutputTokens,
                usage.TotalTokens);
    }

    private void LogTokensUsage(
        IEnumerable<ThreadInitializationMessage> context, string text, RunTokenUsage usage)
    {
        _logger.LogDebug(
                "OpenAI Assistants tokens usage data: {@Context}, {ResponseMessage}, {PromptTokens}, {CompletionTokens}, {TotalTokens}",
                context,
                text,
                usage.PromptTokens,
                usage.CompletionTokens,
                usage.TotalTokens);
    }

    // TODO: Split IDocumentSearch implementation into separate class.

    public async ValueTask UploadDocumentAsync(string storeName, string documentName, Stream document)
    {
        var vectorStoreId = await _vectorStoreMapping.GetVectorStoreIdAsync(storeName);
        VectorStore? vectorStore = null;
        if (vectorStoreId is not null)
        {
            try
            {
                vectorStore = await _vectorClient.GetVectorStoreAsync(vectorStoreId);
            }
            catch (ClientResultException exception) when (exception.Status == 404)
            {
                // Vector store has been deleted on OpenAI side.
                await _vectorStoreMapping.ClearAsync(storeName);
            }
        }

        vectorStore ??= await _vectorClient.CreateVectorStoreAsync(new VectorStoreCreationOptions
        {
            Name = $"foulbot_{storeName}",
            ExpirationPolicy = new VectorStoreExpirationPolicy(VectorStoreExpirationAnchor.LastActiveAt, 30)
        });

        await _vectorStoreMapping.CreateMappingAsync(storeName, vectorStore.Id);

        var file = await _fileClient.UploadFileAsync(document, documentName, FileUploadPurpose.Assistants);
        await _vectorClient.AddFileToVectorStoreAsync(vectorStore, file);
    }

    public async ValueTask ClearStoreAsync(string storeName)
    {
        try
        {
            var vectorStoreId = await _vectorStoreMapping.GetVectorStoreIdAsync(storeName);
            if (vectorStoreId is null)
                return;

            await _vectorClient.DeleteVectorStoreAsync(vectorStoreId);
            await _vectorStoreMapping.ClearAsync(storeName);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            // Vector store has been deleted on OpenAI side.
            await _vectorStoreMapping.ClearAsync(storeName);
            return;
        }
    }

    public async IAsyncEnumerable<DocumentSearchResponse> GetSearchResultsAsync(string storeName, string prompt)
    {
        var vectorStoreId = await _vectorStoreMapping.GetVectorStoreIdAsync(storeName);
        if (vectorStoreId == null)
            yield break;

        try
        {
            await _vectorClient.GetVectorStoreAsync(vectorStoreId);
        }
        catch (ClientResultException exception) when (exception.Status == 404)
        {
            // Vector store has been deleted on OpenAI side.
            await _vectorStoreMapping.ClearAsync(storeName);
            yield break;
        }

        var instructions = "You are an assistant that helps searching data in the documents. When asked to generate some visualization, use the code interpreter tool to do so.";

        AssistantCreationOptions assistantOptions = new()
        {
            Name = "Document Search",
            Instructions = $"{instructions}",
            Tools =
            {
                new FileSearchToolDefinition(),
                new CodeInterpreterToolDefinition()
            },
            ToolResources = new()
            {
                FileSearch = new()
                {
                    VectorStoreIds = [vectorStoreId]
                }
            },
        };

        var assistant = await _assistantClient.CreateAssistantAsync("gpt-4o-mini", assistantOptions);

        ThreadCreationOptions threadOptions = new()
        {
            InitialMessages = { prompt }
        };

        var threadRun = await _assistantClient.CreateThreadAndRunAsync(assistant.Value.Id, threadOptions);

        do
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            threadRun = await _assistantClient.GetRunAsync(threadRun.Value.ThreadId, threadRun.Value.Id);
        } while (!threadRun.Value.Status.IsTerminal);

        LogTokensUsage(threadOptions.InitialMessages, prompt, threadRun.Value.Usage);

        var messagePages = _assistantClient.GetMessagesAsync(threadRun.Value.ThreadId, new MessageCollectionOptions() { Order = ListOrder.OldestFirst });
        var messages = messagePages.GetAllValuesAsync();

        await foreach (var message in messages.Skip(1)) // Skipping the request itself.
        {
            foreach (var contentItem in message.Content)
            {
                if (!string.IsNullOrEmpty(contentItem.Text))
                {
                    yield return new(contentItem.Text, null);

                    // Include annotations, if any.
                    // TODO: Commented out for now.
                    /*foreach (var annotation in contentItem.TextAnnotations)
                    {
                        if (!string.IsNullOrEmpty(annotation.InputFileId))
                        {
                            yield return new($"* File citation, file ID: {annotation.InputFileId}", null);
                        }
                        if (!string.IsNullOrEmpty(annotation.OutputFileId))
                        {
                            yield return new($"* File output, new file ID: {annotation.OutputFileId}", null);
                        }
                    }*/
                }

                if (!string.IsNullOrEmpty(contentItem.ImageFileId))
                {
                    var imageInfo = await _fileClient.GetFileAsync(contentItem.ImageFileId);
                    var imageBytes = await _fileClient.DownloadFileAsync(contentItem.ImageFileId);

                    var stream = imageBytes.Value.ToStream();

                    yield return new(imageInfo.Value.Filename, stream);
                }
            }
        }
    }
}
