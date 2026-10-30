namespace HeyGanesh.Providers.Core;

/// <summary>
/// Contract for a large-language-model provider. All provider-specific code lives in
/// <c>HeyGanesh.Providers.*</c> assemblies; this interface is the only surface other
/// assemblies may depend on.
/// </summary>
public interface ILlmClient : IDisposable
{
    /// <summary>The provider identifier (never a secret).</summary>
    string ProviderId { get; }

    /// <summary>The model identifier currently selected.</summary>
    string ModelId { get; }

    /// <summary>Capability flags the selected model advertises.</summary>
    ProviderCapability Capabilities { get; }

    /// <summary>
    /// Generates a completion for the given request. Honors cancellation and the
    /// configured timeout. Throws <see cref="ProviderException"/> on failure.
    /// </summary>
    Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streams a completion for the given request. Honors cancellation and the
    /// configured timeout. Throws <see cref="ProviderException"/> on failure.
    /// </summary>
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>A single message in an LLM conversation.</summary>
public sealed record LlmMessage
{
    /// <summary>The role: "system", "user", "assistant", or "tool".</summary>
    public string Role { get; init; } = "user";

    /// <summary>Text content.</summary>
    public string? Text { get; init; }

    /// <summary>Image parts (base64 or URL references). Null when text-only.</summary>
    public IReadOnlyList<LlmImagePart>? Images { get; init; }

    /// <summary>Tool call results when Role is "tool".</summary>
    public IReadOnlyList<LlmToolResult>? ToolResults { get; init; }
}

/// <summary>An image part attached to a message.</summary>
public sealed record LlmImagePart
{
    /// <summary>MIME type (e.g. "image/jpeg").</summary>
    public string MimeType { get; init; } = "image/jpeg";

    /// <summary>Base64-encoded image bytes, or a URL.</summary>
    public string Data { get; init; } = string.Empty;
}

/// <summary>A tool definition the model may call.</summary>
public sealed record LlmToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ParametersJsonSchema { get; init; } = "{}";
}

/// <summary>A tool call emitted by the model.</summary>
public sealed record LlmToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = "{}";
}

/// <summary>A tool result supplied by the caller.</summary>
public sealed record LlmToolResult
{
    public string ToolCallId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

/// <summary>Response format specification.</summary>
public sealed record LlmResponseFormat
{
    /// <summary>"text" or "json_schema".</summary>
    public string Type { get; init; } = "text";

    /// <summary>JSON schema when Type is "json_schema".</summary>
    public string? JsonSchema { get; init; }
}

/// <summary>Request to an LLM provider.</summary>
public sealed record LlmRequest
{
    /// <summary>Conversation messages.</summary>
    public IReadOnlyList<LlmMessage> Messages { get; init; } = Array.Empty<LlmMessage>();

    /// <summary>Model override; null uses the client's default.</summary>
    public string? ModelOverride { get; init; }

    /// <summary>Maximum output tokens.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Temperature (0.0–2.0). Null uses provider default.</summary>
    public double? Temperature { get; init; }

    /// <summary>Response format (text or JSON schema).</summary>
    public LlmResponseFormat? ResponseFormat { get; init; }

    /// <summary>Tools the model may call.</summary>
    public IReadOnlyList<LlmToolDefinition>? Tools { get; init; }

    /// <summary>System prompt override.</summary>
    public string? SystemPrompt { get; init; }
}

/// <summary>Response from an LLM provider.</summary>
public sealed record LlmResponse
{
    /// <summary>Generated text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Tool calls emitted by the model.</summary>
    public IReadOnlyList<LlmToolCall> ToolCalls { get; init; } = Array.Empty<LlmToolCall>();

    /// <summary>Model that produced the response.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Input token count.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output token count.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Latency of the call.</summary>
    public TimeSpan Latency { get; init; }
}

/// <summary>A single streamed chunk from an LLM provider.</summary>
public sealed record LlmStreamChunk
{
    /// <summary>Text delta.</summary>
    public string TextDelta { get; init; } = string.Empty;

    /// <summary>Tool call delta (partial).</summary>
    public LlmToolCall? ToolCallDelta { get; init; }

    /// <summary>True when the stream is complete.</summary>
    public bool IsDone { get; init; }

    /// <summary>Final usage stats (only on the last chunk).</summary>
    public int? InputTokens { get; init; }

    /// <summary>Final usage stats (only on the last chunk).</summary>
    public int? OutputTokens { get; init; }
}