using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PidorBot.AI;
using PidorBot.Chats;

namespace PidorBot.Brain;

public enum ReplyKind
{
    /// <summary>
    /// Reply to people talking in the chat.
    /// </summary>
    Reply,

    /// <summary>
    /// The bot writes on its own when the chat is quiet.
    /// </summary>
    Spontaneous,

    /// <summary>
    /// A specific thing to do: greet the chat, remind someone.
    /// </summary>
    Instructed,

    /// <summary>
    /// The bot just reads the chat and maybe reacts to a message.
    /// </summary>
    Glance
}

/// <param name="Photos">Photos attached to context messages, by message id.</param>
/// <param name="Reminders">Reminders set in this chat, for the LLM.</param>
/// <param name="Directives">Orders given with $directive, they override everything else.</param>
public sealed record ReplyRequest(
    ReplyKind Kind,
    BotMode Mode,
    string? Mood,
    string? Memory,
    IReadOnlyList<ChatMessage> Context,
    DateTimeOffset LocalNow,
    string? Instruction = null,
    IReadOnlyDictionary<string, BinaryData>? Photos = null,
    IReadOnlyList<string>? Reminders = null,
    IReadOnlyList<string>? Directives = null);

/// <summary>
/// The bot's turn in the chat: turns everything it knows (mode, mood, memory, recent messages) into actions.
/// The LLM writes text (sent as messages) and calls tools (reactions, stickers, pictures, reminders) for a few rounds.
/// </summary>
public sealed partial class Responder
{
    private const int MaxRounds = 5;
    private const int MaxBadReplies = 3;

    private readonly ILogger<Responder> _logger;
    private readonly ILlm _llm;
    private readonly Random _random;

    public Responder(ILogger<Responder> logger, ILlm llm, Random random)
    {
        _logger = logger;
        _llm = llm;
        _random = random;
    }

    [GeneratedRegex(@"^\[msg \d+\]\s*", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex MessagePrefix();

    [GeneratedRegex(@"\[(?<content>[^\[\]\n]*)\]", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex MimickedToolCall();

    [GeneratedRegex(@"(?<![a-z_])[a-z_]+(?![a-z_])", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex MimickedToolName();

    [GeneratedRegex(@"(?<key>[a-z_]+)\s*=\s*(?:""(?<value>[^""]*)""|(?<value>\S+))", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex MimickedArgument();

    [GeneratedRegex(@"\b\d+\b", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex MimickedMessageId();

    public async Task RunAsync(
        ReplyRequest request, IReadOnlyList<LlmTool> tools, IChatActions actions, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(request).ToList();
        var badReplies = 0;

        for (var round = 1; round <= MaxRounds; round++)
        {
            var response = await _llm.CompleteAsync(prompt, LlmTier.Chat, tools, cancellationToken);
            var (written, mimickedCalls) = ExtractMimickedToolCalls(response.Text, tools);
            if (mimickedCalls.Count > 0)
                _logger.LogWarning("The model wrote tool calls as text, executing them: {Reply}", response.Text);

            IReadOnlyList<LlmToolCall> toolCalls = [.. response.ToolCalls, .. mimickedCalls];
            var text = request.Kind == ReplyKind.Glance ? string.Empty : Clean(written, request.Context);

            if (text.Length > 0)
            {
                var isGood = ReplyFilter.IsGood(text);
                if (!isGood && toolCalls.Count == 0 && ++badReplies < MaxBadReplies)
                {
                    _logger.LogWarning("Generated a bad reply (attempt {Attempt}), regenerating: {Reply}", badReplies, text);
                    continue;
                }

                await actions.SayAsync(text, remember: isGood, cancellationToken);
            }

            if (toolCalls.Count == 0)
                return;

            prompt.Add(new LlmMessage(LlmRole.Assistant, text) { ToolCalls = toolCalls });
            foreach (var call in toolCalls)
            {
                var result = await BotTools.ExecuteAsync(call, actions, cancellationToken);
                _logger.LogInformation("Tool {Tool}({Arguments}): {Result}", call.Name, call.Arguments, result);
                prompt.Add(LlmMessage.ToolResult(call.Id, result));
            }
        }
    }

    public IReadOnlyList<LlmMessage> BuildPrompt(ReplyRequest request)
    {
        var system = new List<string> { request.Mode.Directive, Prompts.Persona, Prompts.Tools };
        if (request.Directives?.Count > 0)
            system.Insert(1, Prompts.Directives(request.Directives));
        if (request.Memory != null)
            system.Add(Prompts.Memory(request.Memory));
        if (request.Reminders != null)
            system.Add(Prompts.Reminders(request.Reminders));

        var styleHint = request.Kind is ReplyKind.Instructed or ReplyKind.Glance || request.Mode.StyleHints.Count == 0
            ? null
            : request.Mode.StyleHints[_random.Next(request.Mode.StyleHints.Count)];

        var hint = Prompts.Hint(request.LocalNow, request.Mood, styleHint);
        var instruction = request.Kind switch
        {
            ReplyKind.Spontaneous => Prompts.Spontaneous,
            ReplyKind.Glance => Prompts.Glance,
            _ => request.Instruction
        };

        // Repeated at the end: orders get lost in a long prompt.
        if (request.Directives?.Count > 0)
            instruction = instruction == null ? Prompts.Directives(request.Directives) : $"{instruction}\n{Prompts.Directives(request.Directives)}";

        return
        [
            new(LlmRole.System, string.Join("\n\n", system)),
            .. request.Context.Select(message => ToLlmMessage(message, request.Photos)),
            new(LlmRole.System, instruction == null ? hint : $"{instruction}\n{hint}")
        ];
    }

    /// <summary>
    /// LLMs sometimes mimic the "[msg N] Name: text" format of the context.
    /// </summary>
    internal static string Clean(string reply, IReadOnlyList<ChatMessage> context)
    {
        reply = MessagePrefix().Replace(reply.Trim(), string.Empty);

        var prefix = context
            .Select(message => message.SenderName)
            .Append(BotIdentity.HistoryName)
            .Append("Пидор Бот")
            .Select(name => $"{name}:")
            .FirstOrDefault(prefix => reply.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        return prefix == null ? reply : reply[prefix.Length..].TrimStart();
    }

    /// <summary>
    /// The model sometimes writes a tool call as text instead of calling it, in whatever format:
    /// "[send_sticker emoji=✋]", "[react on 112165 with 👺]", "[tool call send_sticker with 😎]". Such calls are cut out of the text and executed if the tool is available.
    /// </summary>
    internal static (string Text, IReadOnlyList<LlmToolCall> Calls) ExtractMimickedToolCalls(string reply, IReadOnlyList<LlmTool> tools)
    {
        var calls = new List<LlmToolCall>();
        var text = MimickedToolCall().Replace(reply, match =>
        {
            var content = match.Groups["content"].Value;
            var nameMatch = MimickedToolName().Matches(content).FirstOrDefault(word => tools.Any(tool => tool.Name == word.Value));
            if (nameMatch == null)
                return match.Value;

            var name = nameMatch.Value;
            var rest = content[(nameMatch.Index + nameMatch.Length)..];
            var arguments = new JsonObject();
            foreach (Match argument in MimickedArgument().Matches(rest))
            {
                var value = argument.Groups["value"].Value;
                arguments[argument.Groups["key"].Value] = int.TryParse(value, CultureInfo.InvariantCulture, out var number)
                    ? JsonValue.Create(number)
                    : JsonValue.Create(value);
            }

            // Free-form calls of the simple tools: take the message id and the emoji from wherever they are.
            if (name is BotTools.React or BotTools.SendSticker && !arguments.ContainsKey("emoji")
                && rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(IsEmoji) is { } emoji)
                arguments["emoji"] = TrimAscii(emoji);

            if (name == BotTools.React && !arguments.ContainsKey("message_id")
                && MimickedMessageId().Match(rest) is { Success: true } id)
                arguments["message_id"] = int.Parse(id.Value, CultureInfo.InvariantCulture);

            calls.Add(new LlmToolCall($"call_{Guid.NewGuid():N}", name, arguments.ToJsonString()));
            return string.Empty;
        });

        return (calls.Count == 0 ? reply : text.Trim(), calls);

        static bool IsEmoji(string word)
            => word.Any(c => char.IsSurrogate(c) || char.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol);

        // Quotes, brackets and commas around the emoji.
        static string TrimAscii(string word)
            => word[word.TakeWhile(char.IsAscii).Count()..(word.Length - word.Reverse().TakeWhile(char.IsAscii).Count())];
    }

    private static LlmMessage ToLlmMessage(ChatMessage message, IReadOnlyDictionary<string, BinaryData>? photos)
    {
        if (message.IsFromBot)
            return new(LlmRole.Assistant, message.Text);

        var text = message.TelegramId == null
            ? $"{message.SenderName}: {message.Text}"
            : $"[msg {message.TelegramId}] {message.SenderName}: {message.Text}";

        return photos != null && photos.TryGetValue(message.Id, out var photo)
            ? new(LlmRole.User, text) { Images = [photo] }
            : new(LlmRole.User, text);
    }
}
