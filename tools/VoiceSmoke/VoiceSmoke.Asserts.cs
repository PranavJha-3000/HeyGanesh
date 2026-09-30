using System.Text.Json;
using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>
/// Pure assertions over the protocol layer — recorded frames in, PASS/FAIL out, no sockets and no
/// devices. Each row prints one line per check so a failure names the exact contract that broke.
/// </summary>
internal static partial class VoiceSmokeHost
{
    private static int Check(string name, bool condition, string detail = "")
    {
        Console.WriteLine($"  {(condition ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? " -- " + detail : string.Empty)}");
        return condition ? 0 : 1;
    }

    private static GatewayEventKind Kind(string frame, out GatewayEventDecision decision)
    {
        decision = QwenGatewayProtocol.Classify(frame);
        return decision.Kind;
    }

    private static GatewayEventKind Kind(string frame) => Kind(frame, out _);

    /// <summary>
    /// VO-6: the exact client-message allowlist this module may send, the shape of every builder, and the
    /// classification of every server-event kind the listen loop depends on. Fails when an upstream
    /// protocol rename would otherwise silently break a live listen.
    /// </summary>
    private static int AssertGcpEvents()
    {
        var failures = 0;
        Console.WriteLine("=== VO: GCP event/message contract ===");

        failures += Check("client allowlist has exactly the 7 messages we send", GatewayClientMessage.Allowed.Count == 7,
            string.Join(", ", GatewayClientMessage.Allowed.OrderBy(x => x)));

        using var hello = JsonDocument.Parse(QwenGatewayProtocol.BuildSessionHello(new SessionHelloRequest()));
        failures += Check("session.hello type/protocol", hello.RootElement.GetProperty("type").GetString() == "session.hello" &&
            hello.RootElement.GetProperty("protocol").GetProperty("min").GetString() == QwenGatewayProtocol.WireVersion);
        failures += Check("session.hello declares output_enabled=false (no frontend voice here)",
            !hello.RootElement.GetProperty("connection").GetProperty("output_enabled").GetBoolean());
        failures += Check("session.hello capabilities are input-only + receipts",
            hello.RootElement.GetProperty("capabilities").GetArrayLength() == 3);

        using var audio = JsonDocument.Parse(QwenGatewayProtocol.BuildAudioAppend(new byte[3200], 16000));
        failures += Check("input_audio_buffer.append carries sample_rate 16000 + base64 audio",
            audio.RootElement.GetProperty("type").GetString() == "input_audio_buffer.append" &&
            audio.RootElement.GetProperty("sample_rate").GetInt32() == 16000 &&
            audio.RootElement.GetProperty("audio").GetString()!.Length > 0);

        failures += Check("response.cancel shape", JsonDocument.Parse(QwenGatewayProtocol.BuildResponseCancel())
            .RootElement.GetProperty("type").GetString() == "response.cancel");
        failures += Check("interrupt shape", JsonDocument.Parse(QwenGatewayProtocol.BuildInterrupt())
            .RootElement.GetProperty("type").GetString() == "interrupt");
        failures += Check("session.pong echoes the request id", JsonDocument.Parse(QwenGatewayProtocol.BuildSessionPong("evt_srv_1"))
            .RootElement.GetProperty("request_event_id").GetString() == "evt_srv_1");
        failures += Check("playback.cancelled shape", JsonDocument.Parse(QwenGatewayProtocol.BuildPlaybackCancelled())
            .RootElement.GetProperty("type").GetString() == "playback.cancelled");
        failures += Check("input.suspend.ack names the owner", JsonDocument.Parse(QwenGatewayProtocol.BuildInputSuspendAck("webui"))
            .RootElement.GetProperty("owner").GetString() == "webui");

        // Server frames: the exact names the listen loop switches on.
        failures += Check("session.ready -> SessionLifecycle + stated wire version",
            Kind("{\"type\":\"session.ready\",\"protocolVersion\":\"7.0.0\"}", out var ready) == GatewayEventKind.SessionLifecycle &&
            ready.Version == "7.0.0");
        failures += Check("transcript.final role=user -> UserFinal (the ONLY question source)",
            Kind("{\"type\":\"transcript.final\",\"role\":\"user\",\"content\":\"where is Save\"}") == GatewayEventKind.UserFinal);
        failures += Check("transcript.final role=assistant -> AssistantFinal (never text)",
            Kind("{\"type\":\"transcript.final\",\"role\":\"assistant\",\"content\":\"hi\"}") == GatewayEventKind.AssistantFinal);
        failures += Check("transcript.final without a role -> Unknown (fail-safe, cannot become text)",
            Kind("{\"type\":\"transcript.final\",\"content\":\"??\"}") == GatewayEventKind.Unknown);
        failures += Check("response.started -> AssistantResponseStarted (cancel trigger)",
            Kind("{\"type\":\"response.started\"}") == GatewayEventKind.AssistantResponseStarted);
        failures += Check("audio.delta -> AssistantAudio with audio flagged",
            Kind("{\"type\":\"audio.delta\",\"audio\":\"AAAA\"}", out var delta) == GatewayEventKind.AssistantAudio &&
            delta.CarriesAudio);
        failures += Check("transcript.discard -> TranscriptDiscard",
            Kind("{\"type\":\"transcript.discard\",\"role\":\"user\"}") == GatewayEventKind.TranscriptDiscard);
        failures += Check("session.ping -> SessionPing",
            Kind("{\"type\":\"session.ping\"}") == GatewayEventKind.SessionPing);
        failures += Check("input.suspend -> InputSuspend with owner",
            Kind("{\"type\":\"input.suspend\",\"owner\":\"webui\"}", out var suspend) == GatewayEventKind.InputSuspend &&
            suspend.State == "webui");
        failures += Check("malformed JSON -> Unknown, never a throw",
            Kind("{not json") == GatewayEventKind.Unknown);

        Console.WriteLine(failures == 0 ? "GCP CONTRACT OK (exit 0)" : $"GCP CONTRACT BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// VO-5: the Tutor-authority invariant, replayed from a recorded Gateway stream. Exactly one frame
    /// kind may produce text; everything the frontend generated for itself is counted and dropped. This
    /// is the row that proves DEC-013's cancel/discard design actually holds the line.
    /// </summary>
    private static int AssertTutorAuthority()
    {
        var failures = 0;
        Console.WriteLine("=== VO: Tutor authority (assistant output neutralised) ===");

        var ledger = new GatewaySuppressionLedger();

        // A realistic utterance where the frontend answers anyway: user speech, then its own reply.
        var frames = new[]
        {
            "{\"type\":\"transcript.delta\",\"role\":\"user\",\"content\":\"where is\"}",
            "{\"type\":\"transcript.final\",\"role\":\"user\",\"content\":\"where is the Save button\"}",
            "{\"type\":\"response.started\",\"response_id\":\"resp_1\"}",
            "{\"type\":\"audio.delta\",\"audio\":\"QUJD\"}",
            "{\"type\":\"audio.delta\",\"audio\":\"REVG\"}",
            "{\"type\":\"audio.done\"}",
            "{\"type\":\"transcript.delta\",\"role\":\"assistant\",\"content\":\"The Save button is\"}",
            "{\"type\":\"transcript.final\",\"role\":\"assistant\",\"content\":\"The Save button is in the toolbar.\"}",
            "{\"type\":\"tool.call\",\"name\":\"web_search\",\"surface\":\"frontend\"}",
            "{\"type\":\"client.state\",\"state\":\"idle\"}",
        };

        string? question = null;
        foreach (var frame in frames)
        {
            var decision = ledger.Observe(frame);
            // The single decision rule of the whole in-leg: only user finals may become text.
            if (decision.Kind == GatewayEventKind.UserFinal)
            {
                question = decision.Text;
            }
        }

        failures += Check("exactly one question came from transcript.final role=user",
            question == "where is the Save button");
        failures += Check("assistant final counted, never text", ledger.AssistantFinals == 1);
        failures += Check("assistant audio chunks counted (audio only reaches IAudioOutput via ITtsClient, never from the Gateway)",
            ledger.AssistantAudioChunks == 2);
        failures += Check("frontend tool call counted and ignored", ledger.ToolCalls == 1);
        failures += Check("suppression total >= 3 (text + 2 audio)", ledger.SuppressedAssistantEvents >= 3);
        failures += Check("user finals counted once", ledger.UserFinals == 1);
        failures += Check("report line: " + ledger.Describe(), true);

        Console.WriteLine(failures == 0 ? "TUTOR AUTHORITY OK (exit 0)" : $"TUTOR AUTHORITY BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// VO-7: the Backend Agent guard. AGENT_PROTOCOL=none must mean task.* never arrives; when one does,
    /// it must be named (not ignored) and counted, which arms the listen loop's abort. The health-endpoint
    /// check itself is live-only (it reads the running Gateway's own payload).
    /// </summary>
    private static int AssertNoBackend()
    {
        var failures = 0;
        Console.WriteLine("=== VO: Backend Agent guard ===");

        var ledger = new GatewaySuppressionLedger();
        ledger.Observe("{\"type\":\"task.started\",\"id\":\"task_1\"}");
        failures += Check("task.* classified as TaskEvent, not Unknown",
            QwenGatewayProtocol.Classify("{\"type\":\"task.started\"}").Kind == GatewayEventKind.TaskEvent);
        failures += Check("ledger saw the backend event (arms the abort)", ledger.SawBackendTask);
        failures += Check("task count is 1", ledger.TaskEvents == 1);

        // Normal traffic on a clean ledger must not trip the guard: it is event-driven, not pessimistic.
        var clean = new GatewaySuppressionLedger();
        clean.Observe("{\"type\":\"transcript.final\",\"role\":\"user\",\"content\":\"hi\"}");
        failures += Check("normal traffic does not trip the guard", !clean.SawBackendTask);

        Console.WriteLine(failures == 0 ? "NO-BACKEND GUARD OK (exit 0)" : $"GUARD BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>VO-9: the Fish contract — wav/44100 body, strict WAV validation, and the key gate before any I/O.</summary>
    private static async Task<int> AssertFishFormatAsync()
    {
        var failures = 0;
        Console.WriteLine("=== VO: Fish WAV contract ===");

        using (var probe = new FishTtsClient(FishTtsOptions.Default, new HttpClient(new StubHandler())))
        {
            var body = probe.BuildRequestBody("hello", voiceId: null).ToJsonString();
            failures += Check("body format is wav (the chosen v1 format)", body.Contains("\"format\":\"wav\""));
            failures += Check("body sample_rate is 44100 (documented supported set)", body.Contains("\"sample_rate\":44100"));
            failures += Check("no key material ever enters the body",
                !body.Contains("FISH_AUDIO_API_KEY", StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            // Through the public constructor (the same path a host takes): Validate() is internal by
            // convention here, matching ScreenOverlayOptions.
            using var bad = new FishTtsClient(new FishTtsOptions { Format = "mp3" });
            failures += Check("non-wav format refused at client construction", false);
        }
        catch (ArgumentOutOfRangeException)
        {
            failures += Check("non-wav format refused at client construction", true);
        }

        // WAV parser: the valid fixture parses to its exact byte count and rate, and marks completion.
        long parsedBytes = 0;
        var parsedRate = 0;
        var sawFinal = false;
        await foreach (var chunk in WavPcmReader.ReadAsync(new MemoryStream(WavFixture.Valid()), CancellationToken.None))
        {
            parsedBytes += chunk.Pcm.Length;
            parsedRate = chunk.SampleRate;
            sawFinal = chunk.IsFinal;
        }

        failures += Check("valid WAV parses to 8820 data bytes @ 44100 with a final marker",
            parsedBytes == 8820 && parsedRate == 44100 && sawFinal);

        failures += Check("stereo WAV refused (only mono is playable)",
            await ThrowsVoice(WavPcmReader.ReadAsync(new MemoryStream(WavFixture.Valid(channels: 2)), CancellationToken.None)));
        failures += Check("wrong magic refused (no playing bytes we cannot trust)",
            await ThrowsVoice(WavPcmReader.ReadAsync(new MemoryStream(WavFixture.NotWav()), CancellationToken.None)));

        // Key gate: with the env var cleared, enumeration must throw auth BEFORE any I/O
        // (the StubHandler would throw InvalidOperationException if a request were attempted).
        var saved = Environment.GetEnvironmentVariable("FISH_AUDIO_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("FISH_AUDIO_API_KEY", null);
            using var noKey = new FishTtsClient(FishTtsOptions.Default, new HttpClient(new StubHandler()));
            failures += Check("missing key fails as VoiceAuthenticationException before any request",
                await ThrowsAuth(noKey.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FISH_AUDIO_API_KEY", saved);
        }

        Console.WriteLine(failures == 0 ? "FISH CONTRACT OK (exit 0)" : $"FISH CONTRACT BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<bool> ThrowsVoice(IAsyncEnumerable<TtsAudioChunk> source)
    {
        try
        {
            await foreach (var _ in source)
            {
            }

            return false;
        }
        catch (VoiceAuthenticationException)
        {
            return false; // Auth is a different row's contract; not a pass here, not a fail either.
        }
        catch (VoiceClientException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsAuth(IAsyncEnumerable<TtsAudioChunk> source)
    {
        try
        {
            await foreach (var _ in source)
            {
            }

            return false;
        }
        catch (VoiceAuthenticationException)
        {
            return true;
        }
    }

    /// <summary>An HTTP handler that fails loudly if ever called: offline rows must never do I/O.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline row attempted network I/O — that is itself a test failure.");
    }
}
