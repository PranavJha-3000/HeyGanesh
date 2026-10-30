using System.Text.Json;
using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// TU-11: the wire contract of the first live provider, offline and deterministic. The client is
/// built EXCLUSIVELY through <see cref="TutorClientFactory.CreateDeepSeek"/> with
/// <c>BaseUrl</c> pointed at the host-owned loopback stub — the exact public construction path the
/// App Shell's teaching-leg flip will use (no InternalsVisibleTo, no handler seam, no internal type
/// named). Pinned: the DeepSeek payload fields, the fixed thinking-disabled decision (DEC-017), the
/// error mapping, empty-completion pass-through to the SERVICE, and cancellation. NOT claimed: that
/// the real provider accepts any of it — that is TU-12's live row.
/// </summary>
internal static partial class TutorSmokeHost
{
    /// <summary>Stands in for a credential for this row only; never printed, only its presence is checked.</summary>
    private const string StubKey = "sk-TU11-stub-key-must-never-appear-in-a-body";

    private static async Task<int> RunAssertTutorPayloadAsync()
    {
        var failures = 0;

        // The row owns DEEPSEEK_API_KEY for its duration only, and restores it exactly.
        var realKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            // (0) Missing key: typed 12 family, no request attempted — checked against an
            // unroutable URL, so "never leaves the process" is structural, not inferred.
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            var noKeyClient = TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { BaseUrl = "http://127.0.0.1:1/" });
            try
            {
                await noKeyClient.CompleteAsync(PromptFor("anything"), CancellationToken.None);
                failures = Check(false, "missing-key call was expected to throw", failures);
            }
            catch (TutorAuthenticationException ex)
            {
                failures = Check(ex.Message.Contains("DEEPSEEK_API_KEY"), "missing key is a typed auth fault naming the variable (exit 12 family, no request attempted)", failures);
            }

            using var stub = StubServer.Start();
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", StubKey);
            Console.WriteLine($"  stub      : {stub.Prefix} (loopback-only; key presence=yes length={StubKey.Length} value suppressed)");
            var client = TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { BaseUrl = stub.Prefix });

            // (1) Text-only turn through the REAL service, so the wire text is the service's own
            // deterministic rendering (TU-10); expected strings come from the recording fake on the
            // same request, never from a hand-copied prompt.
            var textRequest = ScreenRequest("what does git rebase actually do to my commits");
            var recorder = new FakeTutorClient(FakeTutorClient.Mode.Canned, "ok");
            await new TutorService(recorder).ExplainAsync(textRequest);
            var expected = recorder.Recorded[0];

            stub.EnqueueCompletion("Rebase replays your commits onto the new base, producing new ids while keeping the changes.");
            var lesson = await new TutorService(client).ExplainAsync(textRequest);

            var req = stub.Requests[0];
            failures = Check(req.Method == "POST" && req.Path == "/chat/completions", "POST /chat/completions — the verified DeepSeek endpoint path", failures);
            failures = Check(req.Authorization == $"Bearer {StubKey}", "Bearer authorization carries the call-time key (compared against the stub's own sentinel, never printed)", failures);
            failures = Check(lesson.ExplanationText.StartsWith("Rebase replays", StringComparison.Ordinal),
                "canned content crosses back as the lesson verbatim, usage/model echoed", failures);

            using var body = JsonDocument.Parse(req.Body);
            var root = body.RootElement;
            failures = Check(root.GetProperty("model").GetString() == "deepseek-flash", "model is deepseek-flash", failures);
            failures = Check(root.GetProperty("max_tokens").GetInt32() == 500, "max_tokens is the prompt's own budget from TutorOptions", failures);
            failures = Check(root.GetProperty("stream").GetBoolean() == false, "stream is explicitly false", failures);
            failures = Check(root.GetProperty("thinking").GetProperty("type").GetString() == "disabled",
                "thinking.type == disabled — the fixed v1 decision, pinned on the wire (DEC-017)", failures);

            var messages = root.GetProperty("messages");
            failures = Check(messages.GetArrayLength() == 2
                          && messages[0].GetProperty("role").GetString() == "system"
                          && messages[1].GetProperty("role").GetString() == "user",
                "exactly two messages: system then user", failures);
            failures = Check(messages[0].GetProperty("content").GetString() == expected.SystemText,
                "system content is the service's rendered contract byte-for-byte", failures);
            failures = Check(messages[1].GetProperty("content").ValueKind == JsonValueKind.String
                          && messages[1].GetProperty("content").GetString() == expected.UserText,
                "text-only turn sends user content as a plain string, exactly the rendered prompt", failures);

            foreach (var absent in new[] { "response_format", "tools", "tool_choice", "temperature", "top_p", "reasoning_effort" })
            {
                failures = Check(!req.Body.Contains($"\"{absent}\"", StringComparison.Ordinal), $"no '{absent}' field — no invented provider policy", failures);
            }

            failures = Check(!req.Body.Contains(StubKey, StringComparison.Ordinal), "the key never appears in the request body", failures);

            // (2) Visual turn: the same rendering, plus the verified two-part content array.
            var bytes = JpegFixture.Tiny();
            var visualRequest = new TutorRequest
            {
                Question = "explain what is shown here",
                Context = new TutorContext
                {
                    Found = true,
                    Description = "an equation editor",
                    BoxOnScreenX = 100, BoxOnScreenY = 200, BoxOnScreenWidth = 300, BoxOnScreenHeight = 150,
                    ConfidenceTier = TutorConfidenceTier.High,
                    SourceLeg = "ElementSelection",
                    Visual = new TutorVisual { JpegBytes = bytes, WidthPx = 640, HeightPx = 360 },
                },
            };
            var visualRecorder = new FakeTutorClient(FakeTutorClient.Mode.Canned, "ok");
            await new TutorService(visualRecorder).ExplainAsync(visualRequest);
            var expectedVisual = visualRecorder.Recorded[0];

            stub.EnqueueCompletion("On screen: an equation with variables. In general: a variable is a name for a value.");
            await new TutorService(client).ExplainAsync(visualRequest);

            var vReq = stub.Requests[1];
            using var vBody = JsonDocument.Parse(vReq.Body);
            var vContent = vBody.RootElement.GetProperty("messages")[1].GetProperty("content");
            failures = Check(vContent.ValueKind == JsonValueKind.Array && vContent.GetArrayLength() == 2,
                "visual turn sends the two-part content array", failures);
            failures = Check(vContent[0].GetProperty("type").GetString() == "text"
                          && vContent[0].GetProperty("text").GetString() == expectedVisual.UserText,
                "text part is the same rendered prompt (visual notice included)", failures);
            failures = Check(vContent[1].GetProperty("type").GetString() == "image_url", "second part is image_url", failures);

            var imageUrl = vContent[1].GetProperty("image_url");
            var url = imageUrl.GetProperty("url").GetString() ?? string.Empty;
            var b64 = Convert.ToBase64String(bytes);
            failures = Check(url == $"data:image/jpeg;base64,{b64}",
                "the data URL carries the exact opaque JPEG bytes — no decode, no re-encode, no resize", failures);
            failures = Check(imageUrl.GetProperty("detail").GetString() == "high", "detail is the options' high-fidelity default", failures);
            failures = Check(vBody.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "disabled",
                "thinking.disabled holds on visual turns too", failures);
            failures = Check(!vContent[0].GetProperty("text").GetString()!.Contains(b64, StringComparison.Ordinal),
                "bytes are never duplicated into the text part", failures);

            // (3) Error mapping straight through the seam: the CLIENT must produce the typed
            // families — the service translates nothing it doesn't already have to.
            failures = await CheckStubFaultAsync(failures, client, stub, 401, """{"error":"unauthorized"}""",
                ex => ex is TutorAuthenticationException && ex.Message.Contains("401") && !ex.Message.Contains(StubKey),
                "401 -> typed auth fault, status named, body never quoted");
            failures = await CheckStubFaultAsync(failures, client, stub, 403, """{"error":"forbidden"}""",
                ex => ex is TutorAuthenticationException && ex.Message.Contains("403"),
                "403 -> typed auth fault (12 stays 12)");
            failures = await CheckStubFaultAsync(failures, client, stub, 429, """{"error":"slow down"}""",
                ex => ex is TutorRateLimitException,
                "429 -> its own type, so a future caller can back off instead of retrying flat out");
            failures = await CheckStubFaultAsync(failures, client, stub, 500, "upstream exploded",
                ex => ex is TutorClientException { StatusCode: 500 } and not TutorAuthenticationException and not TutorRateLimitException
                      && ex.Message.Contains("upstream exploded"),
                "500 -> client fault carrying the status and a snippet of the body");

            // (4) Empty completion: the client returns it empty; the SERVICE calls it a provider failure.
            stub.EnqueueCompletion("");
            try
            {
                await new TutorService(client).ExplainAsync(ScreenRequest("why is the sky blue"));
                failures = Check(false, "empty completion was expected to surface as provider failure", failures);
            }
            catch (TutorClientException ex)
            {
                failures = Check(ex.Message.Contains("empty completion"),
                    "empty content crosses as empty text and the service maps it — the client never pads a lesson", failures);
            }

            // (5) Cancellation: stub delays, the token fires, OCE propagates un-rewrapped (13 family).
            stub.EnqueueCompletion("too late", delay: TimeSpan.FromSeconds(10));
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try
            {
                await client.CompleteAsync(PromptFor("anything"), cts.Token);
                failures = Check(false, "cancelled call was expected to throw", failures);
            }
            catch (OperationCanceledException)
            {
                failures = Check(true, "cancelled provider call propagates as OperationCanceledException, never as text", failures);
            }

            // (5b) Key source (DEC-047): a key source given in the options wins over DEEPSEEK_API_KEY either way.
            // Only the Authorization header is compared, against the row's own sentinel, never printed.
            const string delegateKey = "tu11-delegate-dummy-key";
            stub.EnqueueCompletion("Rebase replays your commits onto the new base.");
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            var delegated = TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { BaseUrl = stub.Prefix, ApiKey = () => delegateKey });
            var servedBefore = stub.Requests.Count;
            try
            {
                await new TutorService(delegated).ExplainAsync(textRequest);
            }
            catch (Exception)
            {
                // Only the request's header matters here, not how the stub's canned reply was read.
            }

            failures = Check(stub.Requests.Count == servedBefore + 1 && stub.Requests[^1].Authorization == $"Bearer {delegateKey}",
                "a key source's key is the bearer sent, with DEEPSEEK_API_KEY unset (DEC-047)", failures);
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", StubKey);
            var silent = TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { BaseUrl = "http://127.0.0.1:1/", ApiKey = () => null });
            try
            {
                await silent.CompleteAsync(PromptFor("anything"), CancellationToken.None);
                failures = Check(false, "a key source returning nothing was expected to be a missing key", failures);
            }
            catch (TutorAuthenticationException)
            {
                failures = Check(true, "a key source returning nothing is a missing key, even with DEEPSEEK_API_KEY set (no request attempted)", failures);
            }

            // (6) The visibility pins (reflection): the shell-facing shape DEC-017 promises.
            var concrete = typeof(TutorService).Assembly.GetType("HeyGanesh.TutorEngine.DeepSeekTutorClient", throwOnError: false);
            failures = Check(concrete is { IsPublic: false }, "the concrete client exists but is NOT public — consumers see the seam", failures);
            var create = typeof(TutorClientFactory).GetMethod("CreateDeepSeek");
            failures = Check(create is not null
                          && create.ReturnType == typeof(ITutorClient)
                          && create.GetParameters() is [{ ParameterType.Name: "DeepSeekTutorOptions" }],
                "CreateDeepSeek(DeepSeekTutorOptions?) -> ITutorClient — one method, seam-typed return", failures);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", realKey);
        }

        Console.WriteLine("NOTE: the stub proves what we SEND and how we read canned replies. It cannot");
        Console.WriteLine("      prove the real provider accepts the payload — that is TU-12, live-gated.");
        return Verdict(failures, "TUTOR PAYLOAD CONTRACT OK");
    }

    private static async Task<int> CheckStubFaultAsync(
        int failures, ITutorClient client, StubServer stub, int status, string body,
        Func<TutorClientException, bool> expect, string label)
    {
        stub.Enqueue(status, body);
        try
        {
            await client.CompleteAsync(PromptFor("anything"), CancellationToken.None);
            return Check(false, $"{label} — nothing was thrown", failures);
        }
        catch (TutorClientException ex)
        {
            return Check(expect(ex), label, failures);
        }
    }

    /// <summary>
    /// A minimal rendered prompt for seam-level rows, produced by the REAL service through the
    /// recording fake. FakeTutorClient completes synchronously, so the recording is already present
    /// when ExplainAsync returns its (already-completed) task — no await needed, no await skipped.
    /// </summary>
    private static TutorPrompt PromptFor(string question)
    {
        var recorder = new FakeTutorClient(FakeTutorClient.Mode.Canned, "ok");
        new TutorService(recorder).ExplainAsync(new TutorRequest { Question = question });
        return recorder.Recorded[0];
    }
}