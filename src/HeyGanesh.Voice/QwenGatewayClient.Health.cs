using System.Text.Json;

namespace HeyGanesh.Voice;

public sealed partial class QwenGatewayClient
{
    /// <summary>
    /// Pre-flight against the Gateway's health endpoint: capability check, backend-protocol check, and
    /// capture of what the Gateway reports about itself. Runs once per connect, before any audio is sent.
    /// </summary>
    /// <remarks>
    /// This is a best-effort assertion layered on top of the runtime guard: it refuses when the Gateway
    /// *reports* a backend protocol or lacks the required capability, and records "unknown" when the
    /// payload says nothing — in which case the <c>task.*</c> abort in the listen loop is still armed
    /// (see DEC-013). An unreachable health endpoint is not fatal by itself: the socket handshake already
    /// proved the Gateway is there, and a health path that moved must not brick voice entirely.
    /// </remarks>
    private async Task AssertGatewaySetupAsync(CancellationToken cancellationToken)
    {
        if (!_gateway.RequireConversationClientCapability && !_gateway.RequireBackendDisabled)
        {
            BackendState = "not checked (checks disabled by options)";
            return;
        }

        if (GatewayEndpoint is null)
        {
            return;
        }

        var uri = _gateway.ResolveHealthUri(new Uri($"{GatewayEndpoint}/"));
        string payload;
        try
        {
            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            if (ex is TaskCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            BackendState = $"unknown (health endpoint unreachable: {ex.GetType().Name})";
            return;
        }

        using var document = ParseHealth(payload);
        if (document is null)
        {
            BackendState = "unknown (health endpoint returned no JSON)";
            return;
        }

        var backend = FindBackendValue(document.RootElement);
        BackendState = backend is null
            ? "unknown (health payload reports no backend field)"
            : string.IsNullOrWhiteSpace(backend) ? "none" : backend;

        if (_gateway.RequireBackendDisabled &&
            backend is not null &&
            !_gateway.BackendDisabledValues.Contains(backend, StringComparer.OrdinalIgnoreCase))
        {
            throw new VoiceProtocolException(
                $"The Gateway reports Backend Agent protocol '{backend}'. HeyGanesh requires AGENT_PROTOCOL=none: " +
                "it must never let an external agent answer for its Tutor Engine. Fix the Gateway's " +
                "AGENT_PROTOCOL setting, or set QwenGatewayOptions.RequireBackendDisabled=false to listen anyway " +
                "(in which case the task.* guard still aborts on any backend event).");
        }

        if (_gateway.RequireConversationClientCapability &&
            !HasCapability(document.RootElement, _gateway.RequiredCapability))
        {
            // Refusing rather than hoping the events still arrive: every consumer of this module is built
            // on named event types, and an unannounced contract is how those names silently stop working.
            throw new VoiceProtocolException(
                $"The Gateway does not advertise capability '{_gateway.RequiredCapability}', so this client " +
                "cannot know it will receive the transcript/response events this module depends on.");
        }
    }

    private static JsonDocument? ParseHealth(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recursively looks for any field whose name matches <see cref="BackendFieldNames"/> and returns its
    /// string value (or "" for an explicit null/empty, meaning disabled). Null means "no such field".
    /// </summary>
    private static string? FindBackendValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (BackendFieldNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.Undefined => string.Empty,
                    _ => property.Value.GetRawText(),
                };
            }

            var nested = property.Value.ValueKind == JsonValueKind.Object
                ? FindBackendValue(property.Value)
                : null;
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>True when the health payload lists <paramref name="capability"/> (flat array, or under capabilities.realtime).</summary>
    private static bool HasCapability(JsonElement root, string capability)
    {
        if (!root.TryGetProperty("capabilities", out var capabilities))
        {
            return false;
        }

        if (capabilities.ValueKind == JsonValueKind.Object &&
            capabilities.TryGetProperty("realtime", out var nested))
        {
            capabilities = nested;
        }

        if (capabilities.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in capabilities.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), capability, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
