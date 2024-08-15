using FoulBot.Domain;
using Google.Cloud.TextToSpeech.V1;
using Microsoft.Extensions.Logging;

namespace FoulBot.Infrastructure;

public sealed class GoogleTtsService : IGoogleTtsService
{
    private readonly ILogger<GoogleTtsService> _logger;

    public GoogleTtsService(ILogger<GoogleTtsService> logger)
    {
        _logger = logger;
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "key.json");
    }

    public async Task<Stream> GetAudioAsync(string text)
    {
        var client = await TextToSpeechClient.CreateAsync();

        // The input to be synthesized, can be provided as text or SSML.
        var input = new SynthesisInput
        {
            Text = text
        };

        // Build the voice request.
        var voiceSelection = new VoiceSelectionParams
        {
            LanguageCode = "ru-RU",
            SsmlGender = SsmlVoiceGender.Male,
            Name = "ru-RU-Standard-D"
        };

        // Specify the type of audio file.
        var audioConfig = new AudioConfig
        {
            AudioEncoding = AudioEncoding.Mp3,
            Pitch = -2.5,
            SpeakingRate = 0.9
        };

        _logger.LogDebug("Synthesizing speech in google, {Count} characters long.", input.Text.Length);

        // Perform the text-to-speech request.
        var response = await client.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);

        // Write the response to the output file.
        var output = new MemoryStream();
        response.AudioContent.WriteTo(output);
        output.Seek(0, SeekOrigin.Begin);

        return output;
    }
}
