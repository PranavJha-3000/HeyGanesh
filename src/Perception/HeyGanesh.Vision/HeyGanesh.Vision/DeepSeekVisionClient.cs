using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.Vision;

/// <summary>
/// TEMPORARY implementation of <see cref="IVisionClient"/> (see DEC-007). Sends Chat Completions
/// requests directly to the DeepSeek API, reading the bearer key from the <c>DEEPSEEK_API_KEY</c>
/// environment variable at call time. Until the API key security proxy module lands this is the
/// stopgap; swap in the proxy via <see cref="VisionOptions.BaseUrl"/> or another
/// <see cref="IVisionClient"/> -- <see cref="VisionService"/> is unchanged either way.
/// </summary>
internal sealed class DeepSeekVisionClient : IVisionClient, IDisposable
{
    private const string ApiKeyEnvVar = "DEEPSEEK_API_KEY";
    private const string ChatCompletionsPath = "chat/completions";

    private readonly VisionOptions _options;
    private readonly VisionPrompt _prompt;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public DeepSeekVisionClient(VisionOptions? options = null)
    {
        _options = options ?? VisionOptions.Default;
        _prompt = new VisionPrompt();
        var baseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _endpoint = new Uri(baseAddress, ChatCompletionsPath);

        _http = new HttpClient(new SocketsHttpHandler(), disposeHandler: true);
        _http.BaseAddress = baseAddress;
        _http.Timeout = _options.PerCallTimeout;
        _http.DefaultRequestHeaders.Accept.Clear();
    }

    public async Task<VisionRawResponse> AskAsync(VisionRequest request, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            // Fail fast and specifically: a missing key is an auth problem, not a model problem.
            throw new VisionAuthenticationException(
                $"DeepSeek API key not found. Set the {ApiKeyEnvVar} environment variable. " +
                "(This direct-key path is temporary -- see DEC-007 pending the API key security proxy.)");
        }

        var messages = _prompt.BuildMessages(
            request.ImageWidth, request.ImageHeight, request.UserQuestion, request.Image, request.ImageDetail);

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = (double)request.Temperature,
        };
        if (request.UseJsonMode) body["response_format"] = new JsonObject { ["type"] = "json_object" };
        if (request.TopP.HasValue) body["top_p"] = (double)request.TopP.Value;

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? (m.GetString() ?? request.Model)
                : request.Model;
            var (promptTokens, completionTokens) = ReadUsage(root);
            var contentText = ExtractContent(root);

            return new VisionRawResponse(contentText, model, promptTokens, completionTokens, TimeSpan.Zero);
        }

        var status = (int)response.StatusCode;
        var bodyText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (status is 401 or 403) throw new VisionAuthenticationException($"Provider rejected credentials (HTTP {status}).");
        if (status == 429) throw new VisionRateLimitException("Provider rate limited the request (HTTP 429).");
        throw new VisionClientException($"DeepSeek returned HTTP {status}: {bodyText}", status);
    }

    private static (int Prompt, int Completion) ReadUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage))
        {
            var p = usage.TryGetProperty("prompt_tokens", out var px) && px.TryGetInt32(out var pv) ? pv : 0;
            var c = usage.TryGetProperty("completion_tokens", out var cx) && cx.TryGetInt32(out var cv) ? cv : 0;
            return (p, c);
        }
        return (0, 0);
    }

    private static string ExtractContent(JsonElement root)
    {
        // OpenAI/DeepSeek Chat Completions: choices[0].message.content
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
            {
                return content.ValueKind == JsonValueKind.String
                    ? (content.GetString() ?? string.Empty)
                    : content.GetRawText();
            }
        }
        return string.Empty;
    }

    public void Dispose() => _http.Dispose();
}
