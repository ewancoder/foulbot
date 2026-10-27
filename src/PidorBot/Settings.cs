using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PidorBot;

public enum LlmProvider
{
    OpenAI,
    Grok
}

public sealed record LlmModel(LlmProvider Provider, string Name)
{
    /// <summary>
    /// Parses "grok/grok-4-1-fast-reasoning" or "openai/gpt-5.4-mini".
    /// </summary>
    public static LlmModel Parse(string value)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Enum.TryParse<LlmProvider>(parts[0], ignoreCase: true, out var provider))
            throw new InvalidOperationException($"Invalid model '{value}', expected 'grok/<model>' or 'openai/<model>'.");

        return new(provider, parts[1]);
    }

    public override string ToString() => $"{Provider}/{Name}";
}

/// <summary>
/// Secrets use the same names as in the old FoulBot (they come from /data/foulbot/secrets.env),
/// everything else is optional and can be overridden with PidorBot__* environment variables.
/// </summary>
public sealed class Settings
{
    public const string DefaultSeqUrl = "http://31.146.143.167:5341";
    private const string DefaultChatModel = "grok/grok-4-1-fast-non-reasoning";
    private const string DefaultThinkingModel = "grok/grok-4-1-fast-reasoning";
    private const string DefaultImageModel = "openai/gpt-image-1-mini";

    public required string TelegramBotToken { get; init; }
    public required string OpenAIKey { get; init; }
    public string? GrokApiKey { get; init; }
    public required string RedisConnectionString { get; init; }
    public string? SeqUrl { get; init; }
    public string? SeqApiKey { get; init; }

    /// <summary>
    /// Writes replies. Non-reasoning: reasoning models overthink the chat and write walls of text.
    /// </summary>
    public LlmModel ChatModel { get; init; } = LlmModel.Parse(DefaultChatModel);
    public float? ChatTemperature { get; init; } = 1.3f;
    public int ChatMaxOutputTokens { get; init; } = 2000;

    /// <summary>
    /// Thinks in the background: comes up with moods and keeps the chat memory.
    /// </summary>
    public LlmModel ThinkingModel { get; init; } = LlmModel.Parse(DefaultThinkingModel);

    /// <summary>
    /// Draws pictures when the bot decides to send one.
    /// </summary>
    public LlmModel ImageModel { get; init; } = LlmModel.Parse(DefaultImageModel);

    /// <summary>
    /// Chance that the bot glances at messages that are not addressed to it, and maybe reacts to them.
    /// </summary>
    public double GlanceChance { get; init; } = 0.2;

    public string VoiceModel { get; init; } = "tts-1";
    public string VoiceName { get; init; } = "onyx";

    /// <summary>
    /// Every Nth reply is a voice message. 0 disables voice.
    /// </summary>
    public int VoiceEveryReplies { get; init; } = 10;

    public bool LlmMoods { get; init; } = true;
    public bool Memory { get; init; } = true;

    /// <summary>
    /// Folder with allowed_chats file and reminders folder (/app in the container).
    /// </summary>
    public string DataDirectory { get; init; } = ".";

    /// <summary>
    /// Time zone of the chat people: the bot knows what time of day it is.
    /// </summary>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public static Settings Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("PidorBot");
        var isDevelopment = environment.IsDevelopment();

        // Locally the test bot is used, same as in the old FoulBot debug mode.
        var token = section["TelegramBotToken"]
            ?? (isDevelopment ? configuration["EwTest1BotApiKey"] : null)
            ?? configuration["EwPidorBotApiKey"];

        var settings = new Settings
        {
            TelegramBotToken = Required(token, "EwPidorBotApiKey"),
            OpenAIKey = Required(configuration["OpenAIKey"], "OpenAIKey"),
            GrokApiKey = configuration["GrokApiKey"],
            RedisConnectionString = Required(configuration["RedisConnectionString"], "RedisConnectionString"),
            SeqUrl = section["SeqUrl"] ?? (isDevelopment ? null : DefaultSeqUrl),
            SeqApiKey = configuration["SeqApiKey"],
            ChatModel = LlmModel.Parse(section["ChatModel"] ?? DefaultChatModel),
            // Empty value disables temperature, some models don't support it.
            ChatTemperature = section["ChatTemperature"] is { } temperature
                ? (temperature.Length == 0 ? null : float.Parse(temperature, System.Globalization.CultureInfo.InvariantCulture))
                : 1.3f,
            ThinkingModel = LlmModel.Parse(section["ThinkingModel"] ?? DefaultThinkingModel),
            ImageModel = LlmModel.Parse(section["ImageModel"] ?? DefaultImageModel),
            GlanceChance = section.GetValue("GlanceChance", 0.2),
            VoiceModel = section["VoiceModel"] ?? "tts-1",
            VoiceName = section["VoiceName"] ?? "onyx",
            VoiceEveryReplies = section.GetValue("VoiceEveryReplies", 10),
            LlmMoods = section.GetValue("LlmMoods", true),
            Memory = section.GetValue("Memory", true),
            DataDirectory = section["DataDirectory"] ?? ".",
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(section["TimeZone"] ?? "Asia/Tbilisi")
        };

        if (new[] { settings.ChatModel, settings.ThinkingModel, settings.ImageModel }.Any(model => model.Provider == LlmProvider.Grok)
            && string.IsNullOrEmpty(settings.GrokApiKey))
            throw new InvalidOperationException("GrokApiKey is required for Grok models.");

        return settings;
    }

    private static string Required(string? value, string name)
        => string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException($"{name} configuration value is missing.")
            : value;
}
