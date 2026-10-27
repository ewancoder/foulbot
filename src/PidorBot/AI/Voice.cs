using OpenAI.Audio;

namespace PidorBot.AI;

public interface IVoice
{
    bool IsEnabled { get; }

    /// <summary>
    /// Returns OGG/Opus audio (Telegram voice message format), or null when the text can't be voiced.
    /// </summary>
    Task<Stream?> SpeakAsync(string text, CancellationToken cancellationToken);
}

public sealed class OpenAIVoice : IVoice
{
    // OpenAI TTS limit is 4096 characters.
    private const int MaxTextLength = 4000;

    private readonly ILogger<OpenAIVoice> _logger;
    private readonly AudioClient _client;
    private readonly GeneratedSpeechVoice _voice;

    public OpenAIVoice(ILogger<OpenAIVoice> logger, Settings settings)
    {
        _logger = logger;
        _client = new AudioClient(settings.VoiceModel, settings.OpenAIKey);
        _voice = new GeneratedSpeechVoice(settings.VoiceName);
        IsEnabled = settings.VoiceEveryReplies > 0;
    }

    public bool IsEnabled { get; }

    public async Task<Stream?> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (text.Length > MaxTextLength)
            return null;

        try
        {
            var speech = await _client.GenerateSpeechAsync(
                text, _voice, new SpeechGenerationOptions { ResponseFormat = GeneratedSpeechFormat.Opus }, cancellationToken);

            return speech.Value.ToStream();
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Could not generate voice, sending text instead");
            return null;
        }
    }
}
