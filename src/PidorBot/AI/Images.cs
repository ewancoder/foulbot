using OpenAI.Images;

namespace PidorBot.AI;

public interface IImageGenerator
{
    /// <summary>
    /// Returns null when the image could not be generated (refused by the model, for example).
    /// </summary>
    Task<byte[]?> GenerateAsync(string prompt, CancellationToken cancellationToken);
}

public sealed class ImageGenerator : IImageGenerator
{
    private static readonly HttpClient _http = new();

    private readonly ILogger<ImageGenerator> _logger;
    private readonly ImageClient _client;
    private readonly LlmModel _model;

    public ImageGenerator(ILogger<ImageGenerator> logger, Settings settings)
    {
        _logger = logger;
        _model = settings.ImageModel;
        _client = new ImageClient(_model.Name, Llm.Credential(settings, _model), Llm.Options(_model, TimeSpan.FromMinutes(3)));
    }

    public async Task<byte[]?> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            // GPT image models always return bytes and don't accept the format parameter.
            var options = _model.Provider == LlmProvider.Grok
                ? new ImageGenerationOptions { ResponseFormat = GeneratedImageFormat.Bytes }
                : new ImageGenerationOptions();

            var image = (await _client.GenerateImageAsync(prompt, options, cancellationToken)).Value;
            if (image.ImageBytes != null)
                return image.ImageBytes.ToArray();

            return image.ImageUri == null ? null : await _http.GetByteArrayAsync(image.ImageUri, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not generate an image for {Prompt}", prompt);
            return null;
        }
    }
}
