using System;
using System.IO;
using System.Threading.Tasks;
using Google.Cloud.TextToSpeech.V1;

namespace FoulBot.Api
{
    public sealed class GoogleTtsService
    {
        public async Task<Stream> GetAudioAsync(string text)
        {
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "key.json");
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
                Pitch = -2,
                SpeakingRate = 0.95
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
}
