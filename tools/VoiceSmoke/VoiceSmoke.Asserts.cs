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
        failures += Check("session.ready as upstream sends it (protocol_version) -> the version is read (BUG-001)",
            Kind("{\"type\":\"session.ready\",\"event_id\":\"evt_s\",\"request_event_id\":\"evt_c\",\"protocol_version\":\"7.0.0\",\"session_id\":\"s1\",\"capabilities\":[]}", out var upstreamReady) == GatewayEventKind.SessionLifecycle &&
            upstreamReady.Version == "7.0.0", $"version={upstreamReady.Version ?? "not stated"}");
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

        // Key source (DEC-046): a key source given in the options wins over FISH_AUDIO_API_KEY either way.
        try
        {
            Environment.SetEnvironmentVariable("FISH_AUDIO_API_KEY", "vo-environment-dummy");
            using var silent = new FishTtsClient(new FishTtsOptions { ApiKey = () => null }, new HttpClient(new StubHandler()));
            failures += Check("a key source returning nothing is a missing key, even with FISH_AUDIO_API_KEY set",
                await ThrowsAuth(silent.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None)));

            Environment.SetEnvironmentVariable("FISH_AUDIO_API_KEY", null);
            var capture = new HeaderCapture();
            using var given = new FishTtsClient(new FishTtsOptions { ApiKey = () => "vo-delegate-dummy" }, new HttpClient(capture));
            try
            {
                await foreach (var _ in given.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None))
                {
                }
            }
            catch (InvalidOperationException)
            {
                // HeaderCapture stops the request after reading its header; no byte leaves the process.
            }

            failures += Check("a key source's key is the bearer sent, with FISH_AUDIO_API_KEY unset",
                capture.Authorization is { } auth && auth.EndsWith("vo-delegate-dummy", StringComparison.Ordinal) && auth.StartsWith("Bearer", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FISH_AUDIO_API_KEY", saved);
        }

        Console.WriteLine(failures == 0 ? "FISH CONTRACT OK (exit 0)" : $"FISH CONTRACT BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>VO-12: the Gnani Timbre contract over a stub: body fields, X-API-Key-ID (no Bearer), key gate before I/O, error mapping, a WAV round trip.</summary>
    private static async Task<int> AssertGnaniTtsAsync()
    {
        var failures = 0;
        Console.WriteLine("=== VO: Gnani Timbre contract ===");

        using (var probe = new GnaniTtsClient(GnaniTtsOptions.Default, new HttpClient(new StubHandler())))
        {
            var body = probe.BuildRequestBody("hello", voice: null).ToJsonString();
            failures += Check("body names model timbre-v3.0 and voice Shivani", body.Contains("\"model\":\"timbre-v3.0\"") && body.Contains("\"voice\":\"Shivani\""));
            failures += Check("body asks for a 48000 Hz mono 16-bit linear_pcm wav", body.Contains("\"sample_rate\":48000") && body.Contains("\"num_channels\":1") && body.Contains("\"sample_width\":2") && body.Contains("\"container\":\"wav\""));
            failures += Check("a request voice overrides the default", probe.BuildRequestBody("x", "Avinash").ToJsonString().Contains("\"voice\":\"Avinash\""));
            failures += Check("no key material enters the body", !body.Contains("GNANI_API_KEY", StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            using var bad = new GnaniTtsClient(new GnaniTtsOptions { Speed = 2.0 });
            failures += Check("speed outside 0.85..1.15 refused at construction", false);
        }
        catch (ArgumentOutOfRangeException)
        {
            failures += Check("speed outside 0.85..1.15 refused at construction", true);
        }

        var saved = Environment.GetEnvironmentVariable("GNANI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GNANI_API_KEY", null);
            using var noKey = new GnaniTtsClient(GnaniTtsOptions.Default, new HttpClient(new StubHandler()));
            failures += Check("missing key fails as VoiceAuthenticationException before any request",
                await ThrowsAuth(noKey.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None)));

            var scripted = new ScriptedHandler(System.Net.HttpStatusCode.OK, WavFixture.Valid());
            using var good = new GnaniTtsClient(new GnaniTtsOptions { ApiKey = () => "vo-gnani-dummy" }, new HttpClient(scripted));
            long bytes = 0;
            await foreach (var chunk in good.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None))
            {
                bytes += chunk.Pcm.Length;
            }

            failures += Check("the key goes in X-API-Key-ID, not Authorization", scripted.ApiKeyHeader == "vo-gnani-dummy" && !scripted.HadAuthorization);
            failures += Check("posts to /api/v1/tts/inference", scripted.Path == "/api/v1/tts/inference");
            failures += Check("a WAV answer plays through WavPcmReader (8820 bytes)", bytes == 8820);

            foreach (var (status, expectAuth, expectRate) in new[]
            {
                (System.Net.HttpStatusCode.Unauthorized, true, false),
                (System.Net.HttpStatusCode.PaymentRequired, true, false),
                (System.Net.HttpStatusCode.TooManyRequests, false, true),
            })
            {
                using var failing = new GnaniTtsClient(new GnaniTtsOptions { ApiKey = () => "vo-gnani-dummy" }, new HttpClient(new ScriptedHandler(status, "{\"success\":false}"u8.ToArray())));
                var auth = false;
                var rate = false;
                try
                {
                    await foreach (var _ in failing.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None))
                    {
                    }
                }
                catch (VoiceAuthenticationException)
                {
                    auth = true;
                }
                catch (VoiceRateLimitException)
                {
                    rate = true;
                }

                failures += Check($"HTTP {(int)status} maps to {(expectAuth ? "authentication" : "rate limit")}", auth == expectAuth && rate == expectRate);
            }

            using var broken = new GnaniTtsClient(new GnaniTtsOptions { ApiKey = () => "vo-gnani-dummy" }, new HttpClient(new ScriptedHandler(System.Net.HttpStatusCode.InternalServerError, "oops"u8.ToArray())));
            var typed = false;
            try
            {
                await foreach (var _ in broken.SynthesizeAsync(new SpeakRequest { Text = "x" }, CancellationToken.None))
                {
                }
            }
            catch (VoiceClientException ex) when (ex.StatusCode == 500)
            {
                typed = true;
            }

            failures += Check("HTTP 500 is a typed VoiceClientException carrying the status", typed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GNANI_API_KEY", saved);
        }

        Console.WriteLine(failures == 0 ? "GNANI TTS CONTRACT OK (exit 0)" : $"GNANI TTS CONTRACT BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Answers every request with one scripted status and body, recording the key header and path.</summary>
    private sealed class ScriptedHandler(System.Net.HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        public string? ApiKeyHeader { get; private set; }

        public bool HadAuthorization { get; private set; }

        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ApiKeyHeader = request.Headers.TryGetValues("X-API-Key-ID", out var v) ? string.Join(",", v) : null;
            HadAuthorization = request.Headers.Authorization is not null;
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    /// <summary>VO-13: the Gnani Prisma listener over a scripted microphone and a stub handler: end-of-speech rule, request shape, silence, errors, cancellation.</summary>
    private static async Task<int> AssertGnaniSttAsync()
    {
        var failures = 0;
        Console.WriteLine("=== VO: Gnani Prisma listener ===");
        const string ok = "{\"success\":true,\"transcript\":\"where is the save button\",\"model\":\"prisma-v3.0\"}";
        var keyed = new GnaniSttOptions { ApiKey = () => "vo-gnani-dummy" };
        var quickVoice = new VoiceOptions { NoSpeechTimeout = TimeSpan.FromSeconds(1), MaxUtterance = TimeSpan.FromSeconds(5) };

        // Pure helpers.
        var wav = GnaniSttListener.BuildWav(new byte[3200], 16000);
        failures += Check("BuildWav writes RIFF/WAVE, 16000 Hz mono, 44 + data bytes",
            wav.Length == 3244 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(wav, 8, 4) == "WAVE" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt16(wav, 22) == 1);
        long parsed = 0;
        await foreach (var chunk in WavPcmReader.ReadAsync(new MemoryStream(wav), CancellationToken.None))
        {
            parsed += chunk.Pcm.Length;
        }

        failures += Check("the clip it builds is a WAV this module's own reader accepts (3200 data bytes)", parsed == 3200);
        failures += Check("Rms is 0 for silence and 3000 for a steady 3000", GnaniSttListener.Rms(new byte[3200]) == 0 && Math.Abs(GnaniSttListener.Rms(ScriptedMicrophone.Block(3000)) - 3000) < 1);
        failures += Check("an empty transcript parses as empty, not as a fault", GnaniSttListener.ParseTranscript("{\"success\":true,\"transcript\":\"\"}") == string.Empty);

        // A missing key fails before the microphone is ever opened.
        var saved = Environment.GetEnvironmentVariable("GNANI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("GNANI_API_KEY", null);
            var mic0 = new ScriptedMicrophone(Enumerable.Repeat(3000, 5));
            using var noKey = new GnaniSttListener(GnaniSttOptions.Default, quickVoice, mic0, new HttpClient(new StubHandler()));
            var threw = false;
            try
            {
                await noKey.ListenOnceAsync(CancellationToken.None);
            }
            catch (VoiceAuthenticationException)
            {
                threw = true;
            }

            failures += Check("a missing key is an authentication fault and the microphone never opens", threw && mic0.Starts == 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GNANI_API_KEY", saved);
        }

        // Speech: 5 quiet, 8 loud, then quiet. Kept: 3 pre-roll + 8 loud + 10 quiet blocks (1 s of quiet ends it).
        var upload = new UploadHandler(System.Net.HttpStatusCode.OK, ok);
        var mic1 = new ScriptedMicrophone(Enumerable.Repeat(0, 5).Concat(Enumerable.Repeat(3000, 8)).Concat(Enumerable.Repeat(0, 15)));
        using (var listener = new GnaniSttListener(keyed, quickVoice, mic1, new HttpClient(upload)))
        {
            var result = await listener.ListenOnceAsync(CancellationToken.None);
            failures += Check("the transcript is the verdict", result.FinalText == "where is the save button" && !result.IsNoSpeech && !result.Failed && result.FrontendId == "gnani-prisma");
            failures += Check("the microphone was started and stopped once", mic1.Starts == 1 && mic1.Stops == 1);
            failures += Check("the clip holds 3 pre-roll + 8 loud + 10 quiet blocks (21 x 3200 bytes)", upload.WavDataBytes == 21 * 3200);
            failures += Check("the request is multipart to /stt/v3 with model, language_code, format and audio_file",
                upload.Path == "/stt/v3" && upload.Text.Contains("prisma-v3.0") && upload.Text.Contains("en-IN,hi-IN") && upload.Text.Contains("codemix") && upload.Text.Contains("name=audio_file"));
            failures += Check("the key goes in X-API-Key-ID, not Authorization", upload.ApiKeyHeader == "vo-gnani-dummy" && !upload.HadAuthorization);
        }

        // Continuous loud speech is cut at MaxClip (2 s here = 20 blocks).
        var capped = new UploadHandler(System.Net.HttpStatusCode.OK, ok);
        var mic2 = new ScriptedMicrophone(Enumerable.Repeat(3000, 40));
        using (var listener = new GnaniSttListener(new GnaniSttOptions { ApiKey = () => "vo-gnani-dummy", MaxClip = TimeSpan.FromSeconds(2) }, quickVoice, mic2, new HttpClient(capped)))
        {
            await listener.ListenOnceAsync(CancellationToken.None);
            failures += Check("a clip never runs past MaxClip (20 blocks)", capped.WavDataBytes == 20 * 3200 && mic2.Stops == 1);
        }

        // Silence is a verdict, with no request sent.
        var mic3 = new ScriptedMicrophone(Enumerable.Repeat(0, 30));
        using (var listener = new GnaniSttListener(keyed, quickVoice, mic3, new HttpClient(new StubHandler())))
        {
            var result = await listener.ListenOnceAsync(CancellationToken.None);
            failures += Check("nobody speaking is IsNoSpeech (not a failure), nothing is sent, the microphone is stopped", result.IsNoSpeech && !result.Failed && mic3.Stops == 1);
        }

        using (var listener = new GnaniSttListener(keyed, quickVoice, new ScriptedMicrophone(Enumerable.Repeat(3000, 3).Concat(Enumerable.Repeat(0, 15))), new HttpClient(new UploadHandler(System.Net.HttpStatusCode.OK, "{\"success\":true,\"transcript\":\"\"}"))))
        {
            var result = await listener.ListenOnceAsync(CancellationToken.None);
            failures += Check("an empty transcript is IsNoSpeech", result.IsNoSpeech && !result.Failed);
        }

        // Provider faults are typed, and the microphone is released each time.
        foreach (var (status, expect) in new[]
        {
            (System.Net.HttpStatusCode.Unauthorized, "auth"),
            (System.Net.HttpStatusCode.PaymentRequired, "auth"),
            (System.Net.HttpStatusCode.TooManyRequests, "rate"),
            (System.Net.HttpStatusCode.InternalServerError, "client"),
        })
        {
            var mic = new ScriptedMicrophone(Enumerable.Repeat(3000, 3).Concat(Enumerable.Repeat(0, 15)));
            using var listener = new GnaniSttListener(keyed, quickVoice, mic, new HttpClient(new UploadHandler(status, "{\"success\":false}")));
            var got = "none";
            try
            {
                await listener.ListenOnceAsync(CancellationToken.None);
            }
            catch (VoiceAuthenticationException)
            {
                got = "auth";
            }
            catch (VoiceRateLimitException)
            {
                got = "rate";
            }
            catch (VoiceClientException ex) when (ex.StatusCode == 500)
            {
                got = "client";
            }

            failures += Check($"HTTP {(int)status} is a typed {expect} fault and the microphone is stopped", got == expect && mic.Stops == 1);
        }

        // Cancellation stops the microphone and propagates.
        var mic4 = new ScriptedMicrophone(Enumerable.Empty<int>(), hang: true);
        using (var listener = new GnaniSttListener(keyed, quickVoice, mic4, new HttpClient(new StubHandler())))
        {
            using var cts = new CancellationTokenSource(150);
            var cancelled = false;
            try
            {
                await listener.ListenOnceAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            failures += Check("cancelling a listen throws OperationCanceledException and stops the microphone", cancelled && mic4.Stops == 1);
        }

        Console.WriteLine(failures == 0 ? "GNANI STT LISTENER OK (exit 0)" : $"GNANI STT LISTENER BROKEN ({failures} failures, exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>A microphone that plays a script of 100 ms blocks (one steady amplitude each) and counts its starts and stops.</summary>
    private sealed class ScriptedMicrophone(IEnumerable<int> amplitudes, bool hang = false) : IMicrophoneSource
    {
        private readonly int[] _script = amplitudes.ToArray();
        private volatile bool _stopped;

        public int SampleRate => 16000;

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public event EventHandler<MicrophoneFrameEventArgs>? Frame;

        public static byte[] Block(int amplitude)
        {
            var pcm = new byte[3200];
            for (var i = 0; i < 1600; i++)
            {
                BitConverter.TryWriteBytes(pcm.AsSpan(i * 2, 2), (short)amplitude);
            }

            return pcm;
        }

        public void Start()
        {
            Starts++;
            _stopped = false;
            if (hang)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                foreach (var amplitude in _script)
                {
                    if (_stopped)
                    {
                        return;
                    }

                    Frame?.Invoke(this, new MicrophoneFrameEventArgs(Block(amplitude)));
                    await Task.Delay(2);
                }
            });
        }

        public void Stop()
        {
            Stops++;
            _stopped = true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Answers with one scripted status and JSON, recording the upload (bytes, path, key header).</summary>
    private sealed class UploadHandler(System.Net.HttpStatusCode status, string json) : HttpMessageHandler
    {
        public string Text { get; private set; } = string.Empty;

        public int WavDataBytes { get; private set; } = -1;

        public string? Path { get; private set; }

        public string? ApiKeyHeader { get; private set; }

        public bool HadAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Text = System.Text.Encoding.Latin1.GetString(bytes);
            var riff = Text.IndexOf("RIFF", StringComparison.Ordinal);
            WavDataBytes = riff >= 0 ? BitConverter.ToInt32(bytes, riff + 40) : -1;
            Path = request.RequestUri?.AbsolutePath;
            ApiKeyHeader = request.Headers.TryGetValues("X-API-Key-ID", out var v) ? string.Join(",", v) : null;
            HadAuthorization = request.Headers.Authorization is not null;
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
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
    /// <summary>Reads the Authorization header of the one request, then refuses it: nothing is ever sent.</summary>
    private sealed class HeaderCapture : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString()
                ?? (request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : null);
            throw new InvalidOperationException("header captured; the request goes no further");
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Offline row attempted network I/O — that is itself a test failure.");
    }
}
