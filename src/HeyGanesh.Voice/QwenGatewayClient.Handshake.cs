using System.Net.WebSockets;
using System.Threading.Channels;

namespace HeyGanesh.Voice;

public sealed partial class QwenGatewayClient
{
    /// <summary>
    /// Field names a health payload might use to report its backend integration. Searched
    /// case-insensitively and recursively because the verified documentation names the concept but not the
    /// JSON key. A hit is treated as the backend's protocol name; a miss means "not reported", which is
    /// recorded as unknown rather than as safe.
    /// </summary>
    private static readonly string[] BackendFieldNames =
        ["backend", "backendProtocol", "backend_protocol", "agentProtocol", "agent_protocol", "agentBackend"];

    /// <summary>
    /// Opens the socket, performs the 7.0 handshake, and refuses to continue if the Gateway is not the
    /// deployment this module is written against.
    /// </summary>
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var uri = _gateway.ResolveGatewayUri();
        var endpoint = QwenGatewayOptions.SafeEndpoint(uri);

        lock (_gate)
        {
            _transportFault = null;
        }

        var socket = new ClientWebSocket();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.ConnectTimeout);

        try
        {
            await socket.ConnectAsync(uri, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw new VoiceClientException(
                $"The Gateway at {endpoint} did not accept a connection within {_options.ConnectTimeout.TotalSeconds:F0}s.");
        }
        catch (WebSocketException ex)
        {
            socket.Dispose();
            throw new VoiceClientException($"The Gateway at {endpoint} could not be reached: {ex.Message}");
        }

        var inbox = Channel.CreateUnbounded<GatewayEventDecision>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        _socket = socket;
        _inbox = inbox;
        GatewayEndpoint = endpoint;

        // The pump's token is the listen's token: the socket's lifetime is the listen's lifetime, so a
        // cancelled listen tears the reader down with it (CloseTransportAsync then disposes the socket).
        _receivePump = ReceivePumpAsync(socket, inbox.Writer, cancellationToken);

        await SendAsync(QwenGatewayProtocol.BuildSessionHello(_gateway.Hello), cancellationToken).ConfigureAwait(false);
        var ready = await WaitForHandshakeAsync(inbox.Reader, cancellationToken).ConfigureAwait(false);

        if (ready.Kind == GatewayEventKind.Error)
        {
            throw new VoiceClientException($"The Gateway refused the session: {ready.ErrorMessage}");
        }

        if (ready.Kind == GatewayEventKind.TaskEvent)
        {
            throw new VoiceProtocolException(
                $"The Gateway handed this client a Backend Agent event ('{ready.EventType}') during the handshake. " +
                "HeyGanesh requires AGENT_PROTOCOL=none; it does not delegate work to a backend agent.");
        }

        VerifyWireVersion(ready);
        await AssertGatewaySetupAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for <c>session.ready</c>, answering heartbeats while it waits. Lifecycle chatter before ready
    /// is normal and skipped; an error, a backend task, a closed socket, or the connect budget ending all
    /// surface as failures rather than as a silent wait.
    /// </summary>
    private async Task<GatewayEventDecision> WaitForHandshakeAsync(
        ChannelReader<GatewayEventDecision> reader,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.ConnectTimeout);

        try
        {
            while (await reader.WaitToReadAsync(budget.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var decision))
                {
                    switch (decision.Kind)
                    {
                        case GatewayEventKind.SessionLifecycle when decision.EventType == GatewayServerEvent.SessionReady:
                            return decision;

                        case GatewayEventKind.Error:
                        case GatewayEventKind.TaskEvent:
                            return decision;

                        case GatewayEventKind.SessionPing:
                            await SendAsync(QwenGatewayProtocol.BuildSessionPong(), cancellationToken).ConfigureAwait(false);
                            break;

                        default:
                            // gateway.connected / voice.connection / voice.* — bookkeeping on the way to ready.
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VoiceClientException(
                $"The Gateway handshake did not complete within {_options.ConnectTimeout.TotalSeconds:F0}s.");
        }

        throw new VoiceClientException(
            _transportFault ?? "The Gateway closed the connection before the handshake completed.");
    }

    /// <summary>
    /// Checks the wire version the Gateway stated, if it stated one. A major-version mismatch is a refusal,
    /// not a warning: the event names this module depends on are the contract, and a different major line
    /// may have renamed them.
    /// </summary>
    private void VerifyWireVersion(GatewayEventDecision ready)
    {
        if (string.IsNullOrWhiteSpace(ready.Version))
        {
            // Not stated is not the same as wrong, but it is worth knowing, so it is recorded for the host.
            GatewayProtocolVersion = "not stated by session.ready";
            return;
        }

        GatewayProtocolVersion = ready.Version;
        var major = ready.Version.Split('.')[0];
        if (!string.Equals(major, QwenGatewayProtocol.WireVersion.Split('.')[0], StringComparison.Ordinal))
        {
            throw new VoiceProtocolException(
                $"The Gateway negotiated wire version '{ready.Version}', but this client speaks " +
                $"{QwenGatewayProtocol.WireVersion}. Refusing to run against a protocol line whose event " +
                "names this module cannot vouch for.");
        }
    }
}
