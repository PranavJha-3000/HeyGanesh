using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace HeyGanesh.Voice;

/// <summary>
/// Fish Audio behind <see cref="ITtsClient"/> — the only class in this repository that knows Fish exists.
/// </summary>
/// <remarks>
/// <b>Key handling</b> extends DEC-007's temporary direct-key arrangement: the bearer token is read from
/// <see cref="FishTtsOptions.ApiKeyEnvVar"/> on every synthesis, never stored, never in a message, log or
/// result object; a missing key is a typed <see cref="VoiceAuthenticationException"/> naming the variable.
/// <b>Streaming here is transport streaming</b>, not generation streaming: the body is consumed as it
/// arrives, but the verified v1 contract makes no first-audio-latency guarantee for POST /v1/tts — no
/// incremental-generation claim is made anywhere. <b>Format</b> is wav @ 44.1 kHz, validated by
/// <see cref="WavPcmReader"/> before anything plays it. An injected <see cref="HttpClient"/> is
/// caller-owned and never disposed here.
/// </remarks>
public sealed class FishTtsClient : ITtsClient
{
    private readonly FishTtsOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public FishTtsClient(FishTtsOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? FishTtsOptions.Default;
        _options.Validate();

        if (httpClient is null)
        {
            _http = new HttpClient(new SocketsHttpHandler(), disposeHandler: true) { Timeout = _options.RequestTimeout };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    /// <summary>
    /// The exact JSON body sent to <c>POST /v1/tts</c>. Public and pure so verification can assert the
    /// format/sample-rate contract without a network call.
    /// </summary>
    public JsonObject BuildRequestBody(string text, string? voiceId)
    {
        var body = new JsonObject
        {
            ["text"] = text,
            ["format"] = _options.Format,
            ["sample_rate"] = _options.SampleRate,
            ["normalize"] = _options.Normalize,
            ["chunk_length"] = _options.ChunkLength,
            ["latency"] = _options.Latency,
        };

        var reference = voiceId ?? _options.DefaultVoiceId;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            body["reference_id"] = reference;
        }

        return body;
    }

    /// <summary>
    /// Synthesises <paramref name="request"/> and streams the WAV payload as PCM chunks. Lazy: the HTTP
    /// call happens when the caller first enumerates.
    /// </summary>
    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(
        SpeakRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Read at call time only (DEC-007): nothing cached on `this`, so the key cannot surface in a
        // dump, a ToString() or a result object — and a missing key fails as auth before any I/O.
        var key = Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new VoiceAuthenticationException(
                $"Fish Audio key not found. Set the {_options.ApiKeyEnvVar} environment variable. " +
                "(This direct-key path is temporary — see DEC-007 pending the API key security proxy.)");
        }

        var endpoint = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), _options.TtsPath.TrimStart('/'));
        var json = BuildRequestBody(request.Text, request.VoiceId).ToJsonString();

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Headers.TryAddWithoutValidation("model", _options.Model);

        // ResponseHeadersRead: read as soon as headers land — transport streaming only (class remarks).
        using var response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var excerpt = bodyText.Length <= 300 ? bodyText : bodyText[..300] + "…";

            if (status is 401 or 403)
            {
                throw new VoiceAuthenticationException($"Fish Audio rejected credentials (HTTP {status}).");
            }

            if (status == 429)
            {
                throw new VoiceRateLimitException("Fish Audio rate limited the request (HTTP 429).");
            }

            throw new VoiceClientException($"Fish Audio returned HTTP {status}: {excerpt}", status);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in WavPcmReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>Disposes the HttpClient only when this client created it.</summary>
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
