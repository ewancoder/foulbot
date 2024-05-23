using System;
using System.IO;
using System.Threading.Tasks;
using Google.Cloud.TextToSpeech.V1;

namespace FoulBot.Api;

public interface IGoogleTtsService
{
    Task<Stream> GetAudioAsync(string text);
}

public sealed class GoogleTtsService : IGoogleTtsService
{
    public GoogleTtsService()
    {
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

        // Perform the text-to-speech request.
        var response = await client.SynthesizeSpeechAsync(input, voiceSelection, audioConfig);

        // Write the response to the output file.
        var output = new MemoryStream();
        response.AudioContent.WriteTo(output);
        output.Seek(0, SeekOrigin.Begin);

        return output;
    }
}
