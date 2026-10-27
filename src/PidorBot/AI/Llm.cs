using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace PidorBot.AI;

public enum LlmRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record LlmToolCall(string Id, string Name, string Arguments);

/// <param name="Parameters">JSON schema of the arguments.</param>
public sealed record LlmTool(string Name, string Description, string Parameters);

public sealed record LlmMessage(LlmRole Role, string Text)
{
    /// <summary>
    /// JPEG images attached to a user message.
    /// </summary>
    public IReadOnlyList<BinaryData> Images { get; init; } = [];

    /// <summary>
    /// Tool calls the assistant made.
    /// </summary>
    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = [];

    /// <summary>
    /// For tool results: which call this is the result of.
    /// </summary>
    public string? ToolCallId { get; init; }

    public static LlmMessage ToolResult(string toolCallId, string result) => new(LlmRole.Tool, result) { ToolCallId = toolCallId };
}

public sealed record LlmResponse(string Text, IReadOnlyList<LlmToolCall> ToolCalls);

public enum LlmTier
{
    /// <summary>
    /// Fast model that talks in the chat.
    /// </summary>
    Chat,

    /// <summary>
    /// Reasoning model for background thinking: moods and memory.
    /// </summary>
    Thinking
}

public interface ILlm
{
    Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, LlmTier tier, IReadOnlyList<LlmTool> tools, CancellationToken cancellationToken);
}

public static class LlmExtensions
{
    public static async Task<string> CompleteTextAsync(
        this ILlm llm, string prompt, LlmTier tier, CancellationToken cancellationToken)
        => (await llm.CompleteAsync([new(LlmRole.User, prompt)], tier, [], cancellationToken)).Text;
}

/// <summary>
/// Both Grok and OpenAI are used through the OpenAI SDK, Grok API is compatible with it.
/// </summary>
public sealed class Llm : ILlm
{
    public static readonly Uri GrokEndpoint = new("https://api.x.ai/v1");
    private const int MaxAttempts = 3;

    private readonly ILogger<Llm> _logger;
    private readonly Settings _settings;
    private readonly ChatClient _chat;
    private readonly ChatClient _thinking;

    public Llm(ILogger<Llm> logger, Settings settings)
    {
        _logger = logger;
        _settings = settings;
        _chat = new ChatClient(settings.ChatModel.Name, Credential(settings, settings.ChatModel), Options(settings.ChatModel, TimeSpan.FromMinutes(1)));
        _thinking = new ChatClient(settings.ThinkingModel.Name, Credential(settings, settings.ThinkingModel), Options(settings.ThinkingModel, TimeSpan.FromMinutes(5)));
    }

    public static ApiKeyCredential Credential(Settings settings, LlmModel model)
        => new(model.Provider == LlmProvider.Grok ? settings.GrokApiKey! : settings.OpenAIKey);

    public static OpenAIClientOptions Options(LlmModel model, TimeSpan timeout) => model.Provider == LlmProvider.Grok
        ? new() { Endpoint = GrokEndpoint, NetworkTimeout = timeout }
        : new() { NetworkTimeout = timeout };

    public async Task<LlmResponse> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, LlmTier tier, IReadOnlyList<LlmTool> tools, CancellationToken cancellationToken)
    {
        var client = tier == LlmTier.Chat ? _chat : _thinking;
        var chatMessages = messages.Select(ToChatMessage).ToList();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Options are created for every call: the SDK modifies them.
                var response = await client.CompleteChatAsync(chatMessages, CreateOptions(tier, tools), cancellationToken);
                var completion = response.Value;
                var text = string.Concat(completion.Content.Select(part => part.Text)).Trim();
                var toolCalls = completion.ToolCalls
                    .Select(call => new LlmToolCall(call.Id, call.FunctionName, call.FunctionArguments.ToString()))
                    .ToList();

                _logger.LogDebug(
                    "LLM {Tier} usage: {PromptTokens} prompt, {CompletionTokens} completion tokens. Response: {Response}, tool calls: {@ToolCalls}",
                    tier, completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount, text, toolCalls);

                return new(text, toolCalls);
            }
            catch (Exception exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(500, 2000) * attempt);
                _logger.LogWarning(exception, "LLM {Tier} request failed, retrying in {Delay}", tier, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private ChatCompletionOptions CreateOptions(LlmTier tier, IReadOnlyList<LlmTool> tools)
    {
        var options = tier == LlmTier.Chat
            ? new ChatCompletionOptions { MaxOutputTokenCount = _settings.ChatMaxOutputTokens, Temperature = _settings.ChatTemperature }
            : new ChatCompletionOptions();

        foreach (var tool in tools)
            options.Tools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.Parameters)));

        return options;
    }

    private static ChatMessage ToChatMessage(LlmMessage message) => message.Role switch
    {
        LlmRole.System => new SystemChatMessage(message.Text),
        LlmRole.User when message.Images.Count > 0 => new UserChatMessage(
            [ChatMessageContentPart.CreateTextPart(message.Text),
            .. message.Images.Select(image => ChatMessageContentPart.CreateImagePart(image, "image/jpeg"))]),
        LlmRole.User => new UserChatMessage(message.Text),
        LlmRole.Assistant when message.ToolCalls.Count > 0 => new AssistantChatMessage(message.ToolCalls
            .Select(call => ChatToolCall.CreateFunctionToolCall(call.Id, call.Name, BinaryData.FromString(call.Arguments)))),
        LlmRole.Assistant => new AssistantChatMessage(message.Text),
        LlmRole.Tool => new ToolChatMessage(message.ToolCallId, message.Text),
        _ => throw new NotSupportedException($"Unknown role {message.Role}.")
    };
}
