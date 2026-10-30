namespace HeyGanesh.Providers.Anthropic;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HeyGanesh.Providers.Core;
using HeyGanesh.Security;

public sealed class AnthropicLlmClient : ILlmClient
{
    private readonly AnthropicOptions _options;
    private readonly HttpClient _http;
    private bool _disposed;

    public string ProviderId => _options.ProviderId;
    public string ModelId => _options.ModelId;
    public ProviderCapability Capabilities => _options.Capabilities;

    public AnthropicLlmClient(AnthropicOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.ModelId))
            throw new ArgumentException("ModelId must not be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new ArgumentException("BaseUrl must not be empty.", nameof(options));
        _http = httpClient ?? new HttpClient { Timeout = _options.Timeout };
    }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var apiKey = GetApiKey();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v1/messages";
        var body = BuildRequestBody(request, stream: false);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        ApplyHeaders(httpRequest, apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, ProviderId, ModelId, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "Transport failure.", ex);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw ClassifyHttpError(response.StatusCode, responseBody, response.Headers);
            }

            return ParseResponse(responseBody);
        }
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var apiKey = GetApiKey();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/v1/messages";
        var body = BuildRequestBody(request, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
        ApplyHeaders(httpRequest, apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorKind.Timeout, ProviderId, ModelId, "Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "Transport failure.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw ClassifyHttpError(response.StatusCode, errorBody, response.Headers);
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Parse SSE events
            var currentEvent = string.Empty;
            string? line;
            var toolInputJson = new StringBuilder();
            bool inToolBlock = false;

            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    // End of event block
                    currentEvent = string.Empty;
                    continue;
                }

                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    currentEvent = line.Substring(6).Trim();
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                var data = line.Substring(5).TrimStart();
                if (string.IsNullOrEmpty(data)) continue;

                LlmStreamChunk? chunk = null;
                try
                {
                    chunk = ParseStreamEvent(currentEvent, data, ref toolInputJson, ref inToolBlock);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (chunk is not null)
                    yield return chunk;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? GetApiKey()
    {
        if (_options.SecretStore is null) return null;
        if (string.IsNullOrWhiteSpace(_options.CredentialProviderId)) return null;
        var handle = _options.SecretStore.Get(_options.CredentialProviderId, _options.CredentialKeyAlias);
        if (handle is null) return null;
        try { return Encoding.UTF8.GetString(handle.AsMemory()); }
        finally { handle.Dispose(); }
    }

    private void ApplyHeaders(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", _options.AnthropicVersion);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private string BuildRequestBody(LlmRequest request, bool stream)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"model\":\"{EscapeJson(_options.ModelId)}\"");

        // max_tokens (required by Anthropic)
        int maxTokens = request.MaxOutputTokens ?? _options.DefaultMaxTokens;
        if (maxTokens <= 0) maxTokens = _options.DefaultMaxTokens;
        sb.Append($",\"max_tokens\":{maxTokens}");

        // System prompt -> top-level system field
        var systemParts = new List<string>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            systemParts.Add(request.SystemPrompt);
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "system" && !string.IsNullOrEmpty(msg.Text))
                systemParts.Add(msg.Text);
        }
        if (systemParts.Count > 0)
        {
            if (systemParts.Count == 1)
                sb.Append($",\"system\":\"{EscapeJson(systemParts[0])}\"");
            else
            {
                sb.Append(",\"system\":[");
                bool sFirst = true;
                foreach (var sp in systemParts)
                {
                    if (!sFirst) sb.Append(',');
                    sFirst = false;
                    sb.Append($"{{\"type\":\"text\",\"text\":\"{EscapeJson(sp)}\"}}");
                }
                sb.Append(']');
            }
        }

        // Messages
        sb.Append(",\"messages\":[");
        var msgs = new List<LlmMessage>();
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "system") continue; // handled above
            msgs.Add(msg);
        }

        // Also handle tool results embedded in messages
        bool first = true;
        foreach (var msg in msgs)
        {
            if (!first) sb.Append(',');
            first = false;
            var role = msg.Role ?? "user";
            // Map roles: only "user" and "assistant" are valid for Anthropic
            if (role != "assistant") role = "user";

            sb.Append($"{{\"role\":\"{EscapeJson(role)}\",\"content\":");

            // Build content array
            var contentParts = new List<string>();

            // Text
            if (!string.IsNullOrEmpty(msg.Text))
                contentParts.Add($"{{\"type\":\"text\",\"text\":\"{EscapeJson(msg.Text)}\"}}");

            // Images
            if (msg.Images is not null)
            {
                foreach (var img in msg.Images)
                {
                    var source = BuildImageSource(img);
                    contentParts.Add($"{{\"type\":\"image\",\"source\":{source}}}");
                }
            }

            // Tool results (when role is tool or has ToolResults)
            if (msg.ToolResults is not null)
            {
                foreach (var tr in msg.ToolResults)
                {
                    contentParts.Add($"{{\"type\":\"tool_result\",\"tool_use_id\":\"{EscapeJson(tr.ToolCallId)}\",\"content\":\"{EscapeJson(tr.Content)}\"}}");
                }
            }

            if (contentParts.Count == 0)
            {
                sb.Append("\"\"");
            }
            else if (contentParts.Count == 1 && msg.Images is null && msg.ToolResults is null)
            {
                // Simple text-only -> can use string shorthand, but content array is safer
                sb.Append('[');
                sb.Append(contentParts[0]);
                sb.Append(']');
            }
            else
            {
                sb.Append('[');
                bool cFirst = true;
                foreach (var cp in contentParts)
                {
                    if (!cFirst) sb.Append(',');
                    cFirst = false;
                    sb.Append(cp);
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        sb.Append(']');

        // Tools
        if (request.Tools is not null && request.Tools.Count > 0)
        {
            sb.Append(",\"tools\":[");
            bool tFirst = true;
            foreach (var tool in request.Tools)
            {
                if (!tFirst) sb.Append(',');
                tFirst = false;
                sb.Append($"{{\"name\":\"{EscapeJson(tool.Name)}\",\"description\":\"{EscapeJson(tool.Description)}\",\"input_schema\":{tool.ParametersJsonSchema}}}");
            }
            sb.Append(']');
        }

        // Structured output
        if (request.ResponseFormat is not null && request.ResponseFormat.Type == "json_schema" && !string.IsNullOrEmpty(request.ResponseFormat.JsonSchema))
        {
            sb.Append($",\"output_config\":{{\"format\":{{\"type\":\"json_schema\",\"schema\":{request.ResponseFormat.JsonSchema}}}}}");
        }

        if (stream)
            sb.Append(",\"stream\":true");

        sb.Append('}');
        return sb.ToString();
    }

    private LlmResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var textParts = new List<string>();
        var toolCalls = new List<LlmToolCall>();

        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (type == "text")
                {
                    if (block.TryGetProperty("text", out var textEl))
                        textParts.Add(textEl.GetString() ?? string.Empty);
                }
                else if (type == "tool_use")
                {
                    var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                    var name = block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    string args = "{}";
                    if (block.TryGetProperty("input", out var inputEl))
                        args = inputEl.GetRawText();
                    toolCalls.Add(new LlmToolCall { Id = id, Name = name, ArgumentsJson = args });
                }
            }
        }

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it)) inputTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) outputTokens = ot.GetInt32();
        }

        var model = ModelId;
        if (root.TryGetProperty("model", out var modelEl))
            model = modelEl.GetString() ?? ModelId;

        return new LlmResponse
        {
            Text = string.Join("", textParts),
            ToolCalls = toolCalls,
            ModelId = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Latency = TimeSpan.Zero,
        };
    }

    private LlmStreamChunk? ParseStreamEvent(string eventType, string data, ref StringBuilder toolInputJson, ref bool inToolBlock)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeEl)) return null;
        var type = typeEl.GetString();

        switch (type)
        {
            case "message_start":
                // Extract input tokens if available
                int? inputTokens = null;
                if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("usage", out var u))
                {
                    if (u.TryGetProperty("input_tokens", out var it)) inputTokens = it.GetInt32();
                }
                return new LlmStreamChunk { TextDelta = string.Empty, InputTokens = inputTokens };

            case "content_block_start":
                if (root.TryGetProperty("content_block", out var cb) && cb.TryGetProperty("type", out var cbType))
                {
                    if (cbType.GetString() == "tool_use")
                    {
                        inToolBlock = true;
                        toolInputJson.Clear();
                        string toolId = cb.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                        string toolName = cb.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                        return new LlmStreamChunk
                        {
                            TextDelta = string.Empty,
                            ToolCallDelta = new LlmToolCall { Id = toolId, Name = toolName, ArgumentsJson = string.Empty },
                        };
                    }
                }
                return new LlmStreamChunk { TextDelta = string.Empty };

            case "content_block_delta":
                if (root.TryGetProperty("delta", out var delta) && delta.TryGetProperty("type", out var deltaType))
                {
                    var dt = deltaType.GetString();
                    if (dt == "text_delta")
                    {
                        var text = delta.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                        return new LlmStreamChunk { TextDelta = text };
                    }
                    else if (dt == "input_json_delta")
                    {
                        var partial = delta.TryGetProperty("partial_json", out var pj) ? pj.GetString() ?? string.Empty : string.Empty;
                        toolInputJson.Append(partial);
                        return new LlmStreamChunk
                        {
                            TextDelta = string.Empty,
                            ToolCallDelta = new LlmToolCall { Id = string.Empty, Name = string.Empty, ArgumentsJson = partial },
                        };
                    }
                }
                return null;

            case "content_block_stop":
                if (inToolBlock)
                {
                    inToolBlock = false;
                    var fullJson = toolInputJson.ToString();
                    if (string.IsNullOrWhiteSpace(fullJson)) fullJson = "{}";
                    return new LlmStreamChunk
                    {
                        TextDelta = string.Empty,
                        ToolCallDelta = new LlmToolCall { Id = string.Empty, Name = string.Empty, ArgumentsJson = fullJson },
                        IsDone = false,
                    };
                }
                return null;

            case "message_delta":
                int? outputTokens = null;
                if (root.TryGetProperty("usage", out var u2))
                {
                    if (u2.TryGetProperty("output_tokens", out var ot)) outputTokens = ot.GetInt32();
                }
                string? stopReason = null;
                if (root.TryGetProperty("delta", out var d) && d.TryGetProperty("stop_reason", out var sr))
                    stopReason = sr.GetString();
                return new LlmStreamChunk
                {
                    TextDelta = string.Empty,
                    IsDone = true,
                    OutputTokens = outputTokens,
                };

            case "message_stop":
                return new LlmStreamChunk { TextDelta = string.Empty, IsDone = true };

            case "ping":
                return null;

            case "error":
                string errMsg = "Stream error";
                if (root.TryGetProperty("error", out var err))
                {
                    if (err.TryGetProperty("message", out var m)) errMsg = m.GetString() ?? errMsg;
                    if (err.TryGetProperty("type", out var et)) errMsg = $"{et.GetString()}: {errMsg}";
                }
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, errMsg);

            default:
                return null;
        }
    }

    private ProviderException ClassifyHttpError(HttpStatusCode statusCode, string body, System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var (kind, retryable) = statusCode switch
        {
            HttpStatusCode.BadRequest => (ProviderErrorKind.InvalidRequest, false),
            HttpStatusCode.Unauthorized => (ProviderErrorKind.AuthFailed, false),
            HttpStatusCode.Forbidden => (ProviderErrorKind.AuthFailed, false),
            HttpStatusCode.NotFound => (ProviderErrorKind.ModelNotFound, false),
            HttpStatusCode.TooManyRequests => (ProviderErrorKind.RateLimited, true),
            HttpStatusCode.RequestTimeout => (ProviderErrorKind.Timeout, true),
            HttpStatusCode.GatewayTimeout => (ProviderErrorKind.Timeout, true),
            (HttpStatusCode)529 => (ProviderErrorKind.RateLimited, true),
            _ when (int)statusCode >= 500 => (ProviderErrorKind.Transport, true),
            _ => (ProviderErrorKind.Transport, false),
        };

        string message = $"{kind} (HTTP {(int)statusCode})";
        TimeSpan? retryAfter = null;

        // Parse Retry-After header
        if (headers.TryGetValues("Retry-After", out var raVals))
        {
            var raStr = raVals.FirstOrDefault();
            if (raStr is not null && int.TryParse(raStr, out var seconds))
                retryAfter = TimeSpan.FromSeconds(seconds);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? message;
                if (err.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var errType = t.GetString() ?? string.Empty;
                    kind = errType switch
                    {
                        "authentication_error" => ProviderErrorKind.AuthFailed,
                        "permission_error" => ProviderErrorKind.AuthFailed,
                        "not_found_error" => ProviderErrorKind.ModelNotFound,
                        "rate_limit_error" => ProviderErrorKind.RateLimited,
                        "timeout_error" => ProviderErrorKind.Timeout,
                        "overloaded_error" => ProviderErrorKind.RateLimited,
                        "invalid_request_error" => ProviderErrorKind.InvalidRequest,
                        _ => kind,
                    };
                }
            }
        }
        catch (JsonException) { }

        return new ProviderException(kind, ProviderId, ModelId, message, isRetryable: retryable, retryAfter: retryAfter);
    }

    private string BuildImageSource(LlmImagePart img)
    {
        var data = img.Data ?? string.Empty;
        if (string.IsNullOrWhiteSpace(data))
            throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Image data must not be empty.");

        // URL form
        if (data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            data.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{{\"type\":\"url\",\"url\":\"{EscapeJson(data)}\"}}";
        }

        // Data URL form
        if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            // Parse data:image/png;base64,XXXX
            var commaIdx = data.IndexOf(',');
            if (commaIdx < 0)
                throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Invalid data URL format.");
            var header = data.Substring(0, commaIdx);
            var base64 = data.Substring(commaIdx + 1);
            var mime = "image/jpeg";
            if (header.StartsWith("data:", StringComparison.Ordinal))
            {
                var semiIdx = header.IndexOf(';');
                if (semiIdx > 5) mime = header.Substring(5, semiIdx - 5);
            }
            if (string.IsNullOrWhiteSpace(base64))
                throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Empty base64 data.");
            return $"{{\"type\":\"base64\",\"media_type\":\"{EscapeJson(mime)}\",\"data\":\"{EscapeJson(base64)}\"}}";
        }

        // Raw base64
        var mimeType = string.IsNullOrWhiteSpace(img.MimeType) ? "image/jpeg" : img.MimeType.Trim();
        if (!mimeType.Contains('/') || mimeType.Length > 64)
            throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Invalid image MIME type.");
        return $"{{\"type\":\"base64\",\"media_type\":\"{EscapeJson(mimeType)}\",\"data\":\"{EscapeJson(data)}\"}}";
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(value.Length + 10);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
}
    }