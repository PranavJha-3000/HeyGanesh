namespace HeyGanesh.Providers.OpenAiCompat;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HeyGanesh.Providers.Core;
using HeyGanesh.Security;

public sealed class OpenAiCompatLlmClient : ILlmClient
{
    private readonly OpenAiCompatOptions _options;
    private readonly HttpClient _http;
    private bool _disposed;

    public string ProviderId => _options.ProviderId;
    public string ModelId => _options.ModelId;
    public ProviderCapability Capabilities => _options.Capabilities;

    public OpenAiCompatLlmClient(OpenAiCompatOptions options, HttpClient? httpClient = null)
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
        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
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
                throw ClassifyHttpError(response.StatusCode, responseBody);
            return ParseResponse(responseBody);
        }
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var apiKey = GetApiKey();
        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
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
                throw ClassifyHttpError(response.StatusCode, errorBody);
            }
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line.Substring(5).TrimStart();
                if (data == "[DONE]") yield break;
                LlmStreamChunk? chunk = null;
                try { chunk = ParseStreamChunk(data); }
                catch (JsonException) { continue; }
                if (chunk is not null) yield return chunk;
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
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        if (!string.IsNullOrWhiteSpace(_options.OrganizationId))
            request.Headers.TryAddWithoutValidation("OpenAI-Organization", _options.OrganizationId);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private string BuildRequestBody(LlmRequest request, bool stream)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"model\":\"{EscapeJson(_options.ModelId)}\"");
        sb.Append(",\"messages\":[");
        var messages = new List<(string role, string? text, IReadOnlyList<LlmImagePart>? images)>();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(("system", request.SystemPrompt, null));
        foreach (var msg in request.Messages)
            messages.Add((msg.Role ?? "user", msg.Text, msg.Images));
        bool first = true;
        foreach (var (role, text, images) in messages)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"{{\"role\":\"{EscapeJson(role)}\",");
            bool hasImages = images is not null && images.Count > 0;
            if (!hasImages)
            {
                sb.Append($"\"content\":\"{EscapeJson(text ?? string.Empty)}\"");
            }
            else
            {
                sb.Append("\"content\":[");
                bool partFirst = true;
                if (!string.IsNullOrEmpty(text))
                {
                    if (!partFirst) sb.Append(',');
                    partFirst = false;
                    sb.Append($"{{\"type\":\"text\",\"text\":\"{EscapeJson(text!)}\"}}");
                }
                foreach (var img in images!)
                {
                    if (!partFirst) sb.Append(',');
                    partFirst = false;
                    var url = BuildImageUrl(img);
                    sb.Append($"{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{EscapeJson(url)}\"}}}}");
                }
                sb.Append(']');
            }
            sb.Append('}');
        }
        sb.Append(']');
        if (request.MaxOutputTokens.HasValue)
            sb.Append($",\"max_tokens\":{request.MaxOutputTokens.Value}");
        if (request.Temperature.HasValue)
            sb.Append($",\"temperature\":{request.Temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (request.ResponseFormat is not null && request.ResponseFormat.Type == "json_schema" && !string.IsNullOrEmpty(request.ResponseFormat.JsonSchema))
            sb.Append($",\"response_format\":{{\"type\":\"json_schema\",\"json_schema\":{request.ResponseFormat.JsonSchema}}}");
        if (request.Tools is not null && request.Tools.Count > 0)
        {
            sb.Append(",\"tools\":[");
            bool tFirst = true;
            foreach (var tool in request.Tools)
            {
                if (!tFirst) sb.Append(',');
                tFirst = false;
                sb.Append($"{{\"type\":\"function\",\"function\":{{\"name\":\"{EscapeJson(tool.Name)}\",\"description\":\"{EscapeJson(tool.Description)}\",\"parameters\":{tool.ParametersJsonSchema}}}}}");
            }
            sb.Append(']');
        }
        if (stream) sb.Append(",\"stream\":true");
        sb.Append('}');
        return sb.ToString();
    }

    private LlmResponse ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = string.Empty;
        var toolCalls = new List<LlmToolCall>();
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var msg))
            {
                if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    text = content.GetString() ?? string.Empty;
                if (msg.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tc.EnumerateArray())
                    {
                        var id = t.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
                        string name = string.Empty, args = "{}";
                        if (t.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var n)) name = n.GetString() ?? string.Empty;
                            if (fn.TryGetProperty("arguments", out var a)) args = a.GetString() ?? "{}";
                        }
                        toolCalls.Add(new LlmToolCall { Id = id, Name = name, ArgumentsJson = args });
                    }
                }
            }
        }
        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) outputTokens = ct.GetInt32();
        }
        var model = ModelId;
        if (root.TryGetProperty("model", out var modelEl))
            model = modelEl.GetString() ?? ModelId;
        return new LlmResponse { Text = text, ToolCalls = toolCalls, ModelId = model, InputTokens = inputTokens, OutputTokens = outputTokens, Latency = TimeSpan.Zero };
    }

    private LlmStreamChunk? ParseStreamChunk(string data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        var textDelta = string.Empty;
        LlmToolCall? toolCallDelta = null;
        bool isDone = false;
        int? inputTokens = null, outputTokens = null;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta))
            {
                if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    textDelta = c.GetString() ?? string.Empty;
                if (delta.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tc.EnumerateArray())
                    {
                        string id = string.Empty, name = string.Empty, args = string.Empty;
                        if (t.TryGetProperty("id", out var idEl)) id = idEl.GetString() ?? string.Empty;
                        if (t.TryGetProperty("function", out var fn))
                        {
                            if (fn.TryGetProperty("name", out var n)) name = n.GetString() ?? string.Empty;
                            if (fn.TryGetProperty("arguments", out var a)) args = a.GetString() ?? string.Empty;
                        }
                        toolCallDelta = new LlmToolCall { Id = id, Name = name, ArgumentsJson = args };
                    }
                }
            }
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                var reason = fr.GetString();
                if (reason is not null) isDone = true;
            }
        }
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
        }
        return new LlmStreamChunk { TextDelta = textDelta, ToolCallDelta = toolCallDelta, IsDone = isDone, InputTokens = inputTokens, OutputTokens = outputTokens };
    }

    private ProviderException ClassifyHttpError(HttpStatusCode statusCode, string body)
    {
        var (kind, retryable) = statusCode switch
        {
            HttpStatusCode.Unauthorized => (ProviderErrorKind.AuthFailed, false),
            HttpStatusCode.Forbidden => (ProviderErrorKind.AuthFailed, false),
            HttpStatusCode.NotFound => (ProviderErrorKind.ModelNotFound, false),
            HttpStatusCode.TooManyRequests => (ProviderErrorKind.RateLimited, true),
            HttpStatusCode.BadRequest => (ProviderErrorKind.InvalidRequest, false),
            _ when (int)statusCode >= 500 => (ProviderErrorKind.Transport, true),
            _ => (ProviderErrorKind.Transport, false),
        };
        string message = $"{kind} (HTTP {(int)statusCode})";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString() ?? message;
                if (err.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var type = t.GetString() ?? string.Empty;
                    if (type.Contains("model", StringComparison.OrdinalIgnoreCase))
                        kind = ProviderErrorKind.ModelNotFound;
                }
            }
        }
        catch (JsonException) { }
        return new ProviderException(kind, ProviderId, ModelId, message, isRetryable: retryable);
    }
    private string BuildImageUrl(LlmImagePart img)
    {
        var data = img.Data ?? string.Empty;
        if (string.IsNullOrWhiteSpace(data))
            throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Image data must not be empty.");

        if (data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            data.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return data;
        }

        var mime = string.IsNullOrWhiteSpace(img.MimeType) ? "image/jpeg" : img.MimeType.Trim();
        if (!mime.Contains('/') || mime.Length > 64)
            throw new ProviderException(ProviderErrorKind.InvalidRequest, ProviderId, ModelId, "Invalid image MIME type.");

        return $"data:{mime};base64,{data}";
    }



    private static string EscapeJson(string value)
    {
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
}