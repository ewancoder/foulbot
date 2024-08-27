namespace FoulBot.Domain.Features;

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
    private readonly FoulChatId _chatId;
    private readonly string _storeName;
    private readonly string _botId;

    public DocumentSearchFeature(
        IDocumentSearch documentSearch,
        IBotMessenger botMessenger,
        FoulChatId chatId,
        string storeName,
        string botId)
    {
        _documentSearch = documentSearch;
        _botMessenger = botMessenger;
        _chatId = chatId;
        _storeName = storeName;
        _botId = botId;
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

            await foreach (var result in _documentSearch.GetSearchResultsAsync(_storeName, text))
            {
                if (result.Image == null && result.Text != null)
                {
                    await _botMessenger.SendTextMessageAsync(_chatId, result.Text);
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
