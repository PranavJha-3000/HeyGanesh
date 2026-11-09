using System.Text;
using System.Text.Json.Nodes;

namespace HeyGanesh.Voice;

/// <summary>
/// Gnani Timbre behind <see cref="ITtsClient"/>: the only class that knows Gnani's TTS exists, beside
/// <see cref="FishTtsClient"/>. One synchronous POST returns a whole WAV (mono PCM16 with a header), which
/// <see cref="WavPcmReader"/> validates before anything plays it; no latency claim is made.
/// </summary>
/// <remarks>
/// Key handling is Fish's (DEC-007/DEC-046): read per call, never cached on this object, a missing key is a
/// <see cref="VoiceAuthenticationException"/> before any I/O. Gnani allows one active request per key at a time:
/// a second overlapping call gets HTTP 429, surfaced as <see cref="VoiceRateLimitException"/>.
/// </remarks>
public sealed class GnaniTtsClient : ITtsClient
{
    private readonly GnaniTtsOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public GnaniTtsClient(GnaniTtsOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? GnaniTtsOptions.Default;
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

    /// <summary>The exact JSON body sent; public and pure so verification asserts the contract without a network call.</summary>
    public JsonObject BuildRequestBody(string text, string? voice) => new()
    {
        ["text"] = text,
        ["voice"] = string.IsNullOrWhiteSpace(voice) ? _options.Voice : voice,
        ["model"] = _options.Model,
        ["language"] = _options.Language,
        ["speed"] = _options.Speed,
        ["audio_config"] = new JsonObject
        {
            ["sample_rate"] = _options.SampleRate,
            ["num_channels"] = 1,
            ["sample_width"] = 2,
            ["encoding"] = "linear_pcm",
            ["container"] = "wav",
        },
    };

    public async IAsyncEnumerable<TtsAudioChunk> SynthesizeAsync(
        SpeakRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = _options.ApiKey is { } apiKey ? apiKey() : Environment.GetEnvironmentVariable(_options.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new VoiceAuthenticationException(
                $"Gnani key not found. Set the {_options.ApiKeyEnvVar} environment variable or store the gnani key. " +
                "(The app reads keys through its key ring, DEC-046: HeyGanesh.App --set-key gnani stores one.)");
        }

        var endpoint = new Uri(new Uri(_options.BaseUrl.TrimEnd('/') + "/"), _options.TtsPath.TrimStart('/'));
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildRequestBody(request.Text, request.VoiceId).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation("X-API-Key-ID", key);

        using var response = await _http
            .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var bodyText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var excerpt = bodyText.Length <= 300 ? bodyText : bodyText[..300] + "…";

            // 402 is "insufficient credit" in Gnani's error table: fixing it needs the account, so it reads as auth (exit 12).
            if (status is 401 or 402 or 403)
            {
                throw new VoiceAuthenticationException($"Gnani rejected the request (HTTP {status}): {excerpt}");
            }

            if (status == 429)
            {
                throw new VoiceRateLimitException("Gnani rate limited the request (HTTP 429): one active request per key at a time.");
            }

            throw new VoiceClientException($"Gnani returned HTTP {status}: {excerpt}", status);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var chunk in WavPcmReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
