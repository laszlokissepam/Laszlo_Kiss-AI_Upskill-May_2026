using System.Text.Json.Serialization;

namespace GardenBuddy.Application.Dial;

public sealed record DialChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("tool_calls")] IReadOnlyCollection<DialToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null);

public sealed record DialToolFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] string Arguments);

public sealed record DialToolCall(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] DialToolFunction Function);

public sealed record DialToolDefinitionFunction(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("parameters")] object Parameters);

public sealed record DialToolDefinition(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] DialToolDefinitionFunction Function);

public sealed record DialChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyCollection<DialChatMessage> Messages,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("top_p")] int TopP,
    [property: JsonPropertyName("max_tokens")] int MaxTokens,
    [property: JsonPropertyName("tools")] IReadOnlyCollection<DialToolDefinition>? Tools = null,
    [property: JsonPropertyName("tool_choice")] object? ToolChoice = null,
    [property: JsonPropertyName("parallel_tool_calls")] bool? ParallelToolCalls = null);

public sealed record DialChatCompletionChoice(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("message")] DialChatMessage Message,
    [property: JsonPropertyName("finish_reason")] string? FinishReason);

public sealed record DialChatCompletionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("choices")] IReadOnlyCollection<DialChatCompletionChoice> Choices);
