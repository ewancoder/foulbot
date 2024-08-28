﻿namespace FoulBot.Domain.Features;

public sealed record DocumentId(string Value);

public sealed record DocumentInfo(
    DocumentId DocumentId, string Name);

public sealed record DocumentSearchResponse(
    string? Text, Stream? Image);

// TODO: Can (and MUST) be STORE scoped.
public interface IDocumentSearch
{
    ValueTask UploadDocumentAsync(string storeName, string documentName, Stream document);
    ValueTask ClearStoreAsync(string storeName);
    //ValueTask<IEnumerable<DocumentInfo>> GetAllDocumentsAsync(string storeName);
    //ValueTask RemoveDocumentAsync(string storeName, DocumentId documentId);

    /// <summary>
    /// Gets ordered items that should be sent in that order.
    /// </summary>
    IAsyncEnumerable<DocumentSearchResponse> GetSearchResultsAsync(string storeName, string prompt);
}

public sealed class DocumentSearchFeature : BotFeature
{
    private readonly IDocumentSearch _documentSearch;
    private readonly IBotMessenger _botMessenger;
    private readonly IFoulAIClient _aiClient;
    private readonly FoulChatId _chatId;
    private readonly string _storeName;
    private readonly string _botId;
    private readonly string _botDirective;

    public DocumentSearchFeature(
        IDocumentSearch documentSearch,
        IBotMessenger botMessenger,
        IFoulAIClient aiClient,
        FoulChatId chatId,
        string storeName,
        string botId,
        string botDirective)
    {
        _documentSearch = documentSearch;
        _botMessenger = botMessenger;
        _aiClient = aiClient;
        _chatId = chatId;
        _storeName = storeName;
        _botId = botId;
        _botDirective = botDirective;
    }

    public override async ValueTask<bool> ProcessMessageAsync(FoulMessage message)
    {
        if (message.Type != FoulMessageType.Document)
        {
            var text = CutKeyword(message.Text, $"@{_botId}");
            text ??= message.Text;

            text = CutKeyword(text, "/search");
            if (text == null)
                return false;

            if (text == "clear")
            {
                await _documentSearch.ClearStoreAsync(_storeName);
                return true;
            }

            var rawText = CutKeyword(text, "raw");

            await foreach (var result in _documentSearch.GetSearchResultsAsync(_storeName, rawText ?? text))
            {
                if (result.Image == null && result.Text != null)
                {
                    if (rawText is not null)
                        await _botMessenger.SendTextMessageAsync(_chatId, result.Text);
                    else
                    {
                        var funText = await _aiClient.GetCustomResponseAsync($"{_botDirective}. Imagine you have produced the following text, update it based on your character but keep the relevant facts intact: \"{result.Text}\"");

                        await _botMessenger.SendTextMessageAsync(_chatId, funText);
                    }
                }

                if (result.Image != null)
                {
                    await _botMessenger.SendImageAsync(_chatId, result.Image);
                }
            }

            return true; // TODO: Actually process requests for document search.
        }

        foreach (var attachment in message.Attachments)
        {
            var fileName = attachment.Name ?? Guid.NewGuid().ToString();

            await _documentSearch.UploadDocumentAsync(
                _storeName, fileName, attachment.Data);

            await _botMessenger.SendTextMessageAsync(_chatId, $"Uploaded file to document search: {fileName}");
        }

        return true;
    }

    public override ValueTask StopFeatureAsync()
    {
        return default;
    }
}