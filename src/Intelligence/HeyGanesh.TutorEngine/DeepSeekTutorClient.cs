using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.TutorEngine;

/// <summary>
/// The first live <see cref="ITutorClient"/> (DEC-017): sends one Chat Completions request to the
/// DeepSeek API per teaching turn, reading the bearer key from the <c>DEEPSEEK_API_KEY</c>
/// environment variable at call time — DEC-007's temporary direct-key stopgap, extended through a
/// second seam. Constructed via <see cref="TutorClientFactory.CreateDeepSeek"/>, and deliberately
/// NOT public: the seam interface is the contract, this class is one way to satisfy it.
/// </summary>
/// <remarks>
/// <para><b>What crosses:</b> the rendered prompt and nothing else — system + user messages,
/// <c>max_tokens</c> from the prompt's budget, <c>stream: false</c>, and the fixed v1 decision
/// <c>thinking: {"type": "disabled"}</c> (the answer will be spoken; reasoning defaults would spend
/// latency and the output budget on a trace instead of the lesson — DEC-017). No tools, no JSON
/// mode, no sampling fields, no retries: every one of those would invent provider policy the
/// teaching contract never asked for.</para>
///
/// <para><b>What comes back:</b> <c>choices[0].message.content</c> verbatim plus best-effort usage
/// and model echo. No parsing, repairing, trimming or truncating — the service owns what a valid
/// lesson is; an empty completion crosses as empty text and is the service's provider failure, never
/// this client's.</para>
///
/// <para><b>Key hygiene:</b> the key is read at call time, never stored in a field, never placed in
/// the payload or a log, never echoed in an exception. Error bodies are snipped before being quoted
/// into fault messages; TU-6 pins that a sentinel credential never reaches the console.</para>
///
/// <para><b>Failure mapping (seam contract):</b> missing key / 401 / 403 →
/// <see cref="TutorAuthenticationException"/> (exit 12); 429 → <see cref="TutorRateLimitException"/>;
/// other non-2xx → <see cref="TutorClientException"/> carrying the status. Transport faults are left
/// to surface as the <c>HttpRequestException</c>/<see cref="TimeoutException"/> families
/// <see cref="TutorService"/> already translates. Cancellation propagates un-rewrapped (exit 13).
/// This client adds no translation of its own — a second translator would be a second authority.</para>
/// </remarks>
internal sealed class DeepSeekTutorClient : ITutorClient
{
    private const string ApiKeyEnvVar = "DEEPSEEK_API_KEY";
    private const string ChatCompletionsPath = "chat/completions";

    /// <summary>Upper bound on how much of an error body is quoted into a fault message.</summary>
    private const int ErrorBodySnippetChars = 500;

    private readonly DeepSeekTutorOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public DeepSeekTutorClient(DeepSeekTutorOptions? options = null)
    {
        _options = options ?? DeepSeekTutorOptions.Default;
        var baseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _endpoint = new Uri(baseAddress, ChatCompletionsPath);

        _http = new HttpClient(new SocketsHttpHandler(), disposeHandler: true);
        _http.BaseAddress = baseAddress;
        _http.Timeout = _options.PerCallTimeout;
        _http.DefaultRequestHeaders.Accept.Clear();
    }

    public async Task<TutorCompletion> CompleteAsync(TutorPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var key = _options.ApiKey is { } apiKey ? apiKey() : Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            // Fail fast and specifically: a missing key is an auth problem, not a model problem, and
            // no byte of the lesson request leaves the process because of it.
            throw new TutorAuthenticationException(
                $"DeepSeek API key not found. Set the {ApiKeyEnvVar} environment variable or store the deepseek key. " +
                "(The app reads keys through its key ring, DEC-046: HeyGanesh.App --set-key stores one.)");
        }

        var messages = DeepSeekTutorMessages.Build(prompt);
        DeepSeekTutorMessages.ApplyImageDetail(messages, _options.ImageDetail);

        var body = new JsonObject
        {
            ["model"] = _options.ModelId,
            ["messages"] = messages,
            ["max_tokens"] = prompt.MaxOutputTokens,
            ["stream"] = false,

            // Fixed v1 provider decision (DEC-017): DeepSeek's deepseek-flash currently defaults to
            // thinking enabled at high effort. A spoken lesson needs predictable latency and a
            // max_tokens budget that bounds the LESSON, so reasoning stays off — on purpose, in the
            // payload, not exposed as a knob.
            ["thinking"] = new JsonObject { ["type"] = "disabled" },
        };

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            if (status is 401 or 403)
            {
                // Status only, body never quoted: an auth response is exactly where a provider might
                // echo something identifying, and exit 12 must not cost a credential.
                throw new TutorAuthenticationException($"Provider rejected credentials (HTTP {status}).");
            }

            if (status == 429)
            {
                throw new TutorRateLimitException("Provider rate limited the request (HTTP 429).");
            }

            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new TutorClientException($"DeepSeek returned HTTP {status}: {Snip(bodyText)}", status);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var text = ExtractContent(root);
        var (inputTokens, outputTokens) = ReadUsage(root);
        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
            ? (m.GetString() ?? _options.ModelId)
            : _options.ModelId;

        // Empty stays empty: the service calls that a provider failure. Never inventing or padding an
        // answer is the same rule the vision leg lives by.
        return new TutorCompletion
        {
            Text = text,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelId = model,
        };
    }

    private static (int Input, int Output) ReadUsage(JsonElement root)
    {
        if (root.TryGetProperty("usage", out var usage))
        {
            var i = usage.TryGetProperty("prompt_tokens", out var ix) && ix.TryGetInt32(out var iv) ? iv : 0;
            var o = usage.TryGetProperty("completion_tokens", out var ox) && ox.TryGetInt32(out var ov) ? ov : 0;
            return (i, o);
        }

        return (0, 0);
    }

    private static string ExtractContent(JsonElement root)
    {
        // OpenAI/DeepSeek Chat Completions: choices[0].message.content. A non-string content (some
        // providers use null on refusal-ish turns) reads as empty — the service maps emptiness.
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string Snip(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty body)";
        }

        return value.Length <= ErrorBodySnippetChars ? value : value[..ErrorBodySnippetChars] + "…";
    }

    /// <summary>Disposes only what this client created — the HttpClient. (TU-9: caller-owned pattern.)</summary>
    public void Dispose() => _http.Dispose();
}