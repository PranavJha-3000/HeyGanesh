using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// TEMPORARY implementation of <see cref="IJevClient"/> (see DEC-008; extends the DEC-007 stopgap
/// pattern to the second provider key). Sends one System One request to TypeSafe's
/// <c>POST {BaseUrl}/v1/systemone</c>, reading the bearer key from the <c>TYPESAFE_API_KEY</c>
/// environment variable at call time only. There is no .NET SDK for TypeSafe (Python/JS only), so
/// this is plain BCL HttpClient. Swap in the proxy via <see cref="SelectionOptions.BaseUrl"/> or
/// another <see cref="IJevClient"/> — <see cref="ElementSelectionService"/> is unchanged either way.
/// </summary>
internal sealed partial class TypeSafeJevClient : IJevClient, IDisposable
{
    private const string ApiKeyEnvVar = "TYPESAFE_API_KEY";
    private const string SystemOnePath = "v1/systemone";

    private readonly SelectionOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public TypeSafeJevClient(SelectionOptions? options = null)
    {
        _options = options ?? SelectionOptions.Default;
        var baseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _endpoint = new Uri(baseAddress, SystemOnePath);

        _http = new HttpClient(new SocketsHttpHandler(), disposeHandler: true);
        _http.Timeout = _options.PerCallTimeout;
        _http.DefaultRequestHeaders.Accept.Clear();
    }

    public async Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            // Fail fast and specifically: a missing key is an auth problem, not a model problem.
            throw new JevAuthenticationException(
                $"TypeSafe API key not found. Set the {ApiKeyEnvVar} environment variable. " +
                "(This direct-key path is temporary -- see DEC-008 pending the API key security proxy.)");
        }

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["state"] = request.State?.DeepClone(),
            ["questions"] = BuildQuestions(request.Questions),
        };

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = content };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var started = Stopwatch.GetTimestamp();

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return ParseResponse(doc.RootElement, request, Stopwatch.GetElapsedTime(started));
        }

        int status = (int)response.StatusCode;
        var bodyText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (status is 401 or 403) throw new JevAuthenticationException($"TypeSafe rejected credentials (HTTP {status}).");
        if (status == 429) throw new JevRateLimitException("TypeSafe rate limited the request (HTTP 429).");
        // 400 invalid request / max_tokens_exceeded, 422 field required, 529 overloaded — surface the body.
        throw new JevClientException($"TypeSafe returned HTTP {status}: {bodyText}", status);
    }

    public void Dispose() => _http.Dispose();
}
