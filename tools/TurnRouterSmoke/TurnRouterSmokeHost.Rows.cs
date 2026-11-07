using HeyGanesh.ElementSelection;
using HeyGanesh.TurnRouter;
using System.Text.RegularExpressions;
using Router = HeyGanesh.TurnRouter.TurnRouter;

namespace HeyGanesh.Tools.TurnRouterSmoke;

internal static partial class TurnRouterSmokeHost
{
    private const string Intent = Router.IntentQuestionId;

    // TR-1: the router is policy only. Keys, sockets and the screen belong to the client the caller hands it.
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var dir = Path.Combine(root!, "src", "Intelligence", "HeyGanesh.TurnRouter");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.TurnRouter.csproj"));
        var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText).ToList();
        string[] needles = { "DllImport", "LibraryImport", "HttpClient", "System.Net", "File.", "Directory.", "Environment.", "Process.", "Console.", "Microsoft.Win32", "Socket", "API_KEY" };
        var found = needles.Where(n => sources.Any(s => s.Contains(n, StringComparison.Ordinal))).ToArray();
        var refs = Regex.Matches(csproj, "<ProjectReference Include=\"[^\"]*\\\\([^\\\\\"]+)\\.csproj\"").Select(m => m.Groups[1].Value).ToArray();

        var failures = 0;
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal), "zero PackageReference", failures);
        failures = Check(refs.SequenceEqual(new[] { "HeyGanesh.ElementSelection" }), $"references Element Selection only, for IJevClient ({string.Join(", ", refs)})", failures);
        failures = Check(found.Length == 0, $"no P/Invoke, network, file, environment, process, console, registry or key use ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }

    // TR-2: one call, one question, and it can stand alone (ids are never shown to Jev). Since DEC-075 no
    // dimension or model question: the drawing program picks 2D or 3D itself.
    private static async Task<int> RunAssertRequestAsync()
    {
        var failures = 0;
        const string Question = "how does a four-stroke engine work?";
        var client = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "lesson", 0.9)));
        await new Router(client).RouteAsync(Request(Question, found: false), CancellationToken.None);

        failures = Check(client.Recorded.Count == 1, $"exactly one Jev call per turn ({client.Recorded.Count})", failures);
        var req = client.Recorded[0];
        failures = Check(req.Model == "jev-1.13.0", $"model id is the Jev pin ({req.Model})", failures);
        failures = Check(req.Questions.Keys.SequenceEqual(new[] { Intent }), $"one question, the intent: no dimension or model question ({string.Join(", ", req.Questions.Keys)})", failures);
        failures = Check(req.Questions[Intent].Type == "choice", "the intent is a typed choice", failures);
        failures = Check(req.Questions[Intent].Instructions.Contains(Question, StringComparison.Ordinal), "the user's question is quoted in the instruction", failures);
        failures = Check(Keys(req, Intent).SequenceEqual(new[] { "explain", "lesson", "point" }), $"intent options are point / explain / lesson ({string.Join(", ", Keys(req, Intent))})", failures);
        var lesson = req.Questions[Intent].Criteria!["lesson"]!;
        failures = Check(lesson.Contains("3D", StringComparison.Ordinal) && !lesson.Contains("card", StringComparison.OrdinalIgnoreCase),
            $"the lesson option offers a drawing, flat or 3D, and no longer cards ({lesson})", failures);

        var state = req.State!.AsObject();
        failures = Check(
            (string?)state["screen_match"] == "not found" && (int?)state["question_words"] == 6 && (string?)state["window_title"] == "Notes - Physics" && state.Count == 3,
            $"facts computed in code travel as state, and only those three ({state.ToJsonString()})", failures);
        failures = Check(!state.ToJsonString().Contains(Question, StringComparison.Ordinal), "the question is not repeated in the state", failures);

        var bare = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "explain", 0.9)));
        await new Router(bare).RouteAsync(Request("what is entropy?"), CancellationToken.None);
        failures = Check((string?)bare.Recorded[0].State!["screen_match"] == "not checked", "an unknown screen match is said as 'not checked', never guessed", failures);

        var custom = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "point", 0.9)));
        await new Router(custom, new TurnRouterOptions { ModelId = "jev-test" }).RouteAsync(Request("where is Save?"), CancellationToken.None);
        failures = Check(custom.Recorded[0].Model == "jev-test", "TurnRouterOptions.ModelId is what is sent", failures);
        return Summarize(failures, "REQUEST OK");
    }

    // TR-3: every answer maps to exactly one route, and Jev can never make the router name something it was not offered.
    private static async Task<int> RunAssertMapAsync()
    {
        var failures = 0;
        var r = Request("teach me");

        var point = await RouteWith(r, (Intent, "point", 0.9));
        failures = Check(point.Routed && point.Intent == TurnIntent.Point && point.ToString() == "point", $"point -> point ({point})", failures);

        var explain = await RouteWith(r, (Intent, "explain", 0.9));
        failures = Check(explain.Routed && explain.Intent == TurnIntent.Explain, $"explain -> explain ({explain})", failures);

        var lesson = await RouteWith(r, (Intent, "lesson", 0.9));
        failures = Check(lesson.Routed && lesson.Intent == TurnIntent.Lesson && lesson.Reason is null && lesson.ToString() == "lesson", $"lesson -> lesson, no canvas named ({lesson})", failures);
        failures = Check(lesson.IntentScore == 0.9, "the intent score is reported", failures);
        failures = Check(lesson.JevModel == "jev-1.13.0" && lesson.InputTokens == 420, "the provider's model and token counts are carried", failures);

        // An answer to a question the router no longer asks (an old dimension or model id) changes nothing.
        var stray = await RouteWith(r, (Intent, "lesson", 0.9), ("dimension", "4d", 0.99), ("model", "jet-engine", 0.99));
        failures = Check(stray.Routed && stray.Intent == TurnIntent.Lesson, $"answers to questions never asked are ignored ({stray})", failures);

        var badIntent = await RouteWith(r, (Intent, "draw", 0.99));
        failures = Check(!badIntent.Routed && badIntent.Reason!.Contains("never offered", StringComparison.Ordinal), $"an intent never offered refuses the route ({badIntent.Reason})", failures);

        var missing = await RouteWith(r);
        failures = Check(!missing.Routed && missing.Reason!.Contains("no answer", StringComparison.Ordinal), $"no intent answer -> no route ({missing.Reason})", failures);
        return Summarize(failures, "MAP OK");
    }

    // TR-4: the 0.5 gate. Unsure about the intent -> no route.
    private static async Task<int> RunAssertGateAsync()
    {
        var failures = 0;
        var r = Request("teach me");

        var low = await RouteWith(r, (Intent, "lesson", 0.49));
        failures = Check(!low.Routed && low.Intent is null && low.Reason!.Contains("below 0.50", StringComparison.Ordinal), $"intent at 0.49 -> no route ({low.Reason})", failures);

        var edge = await RouteWith(r, (Intent, "point", 0.5));
        failures = Check(edge.Routed && edge.Intent == TurnIntent.Point, "intent at exactly 0.50 is acted on (the Medium tier boundary)", failures);

        var noScore = await RouteWith(r, (Intent, "explain", null));
        failures = Check(!noScore.Routed && noScore.IntentScore == 0, "an answer with no confidence and no probability scores 0 and is not acted on", failures);

        var byProbability = await new Router(new FakeJevClient(_ => new JevRawResponse("jev-1.13.0",
            new Dictionary<string, JevAnswer> { [Intent] = new("choice", "explain", null, null, new Dictionary<string, double> { ["explain"] = 0.7, ["lesson"] = 0.2, ["point"] = 0.1 }) },
            10, 0, TimeSpan.Zero))).RouteAsync(r, CancellationToken.None);
        failures = Check(byProbability.Routed && byProbability.IntentScore == 0.7, "with no confidence, the chosen option's probability is the score", failures);

        var strict = await new Router(new FakeJevClient(_ => FakeJevClient.Answers((Intent, "point", 0.7))), new TurnRouterOptions { MinScore = 0.8 }).RouteAsync(r, CancellationToken.None);
        failures = Check(!strict.Routed, "TurnRouterOptions.MinScore moves the gate", failures);
        return Summarize(failures, "GATE OK");
    }

    // TR-5: the router never swallows a provider fault: the caller decides what a failed router means.
    private static async Task<int> RunAssertErrorsAsync()
    {
        var failures = 0;
        Exception[] faults =
        {
            new JevAuthenticationException("401 from provider"),
            new JevRateLimitException("429 from provider"),
            new JevClientException("500 from provider", 500),
        };

        foreach (var fault in faults)
        {
            Exception? caught = null;
            try
            {
                await new Router(new FakeJevClient(fault)).RouteAsync(Request("x"), CancellationToken.None);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            failures = Check(ReferenceEquals(caught, fault), $"{fault.GetType().Name} passes through unchanged", failures);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = false;
        var client = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "point", 0.9)));
        try
        {
            await new Router(client).RouteAsync(Request("x"), cts.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        failures = Check(cancelled && client.Recorded.Count == 0, "a cancelled token throws OperationCanceledException and nothing is sent", failures);

        var blank = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "point", 0.9)));
        var argRefused = false;
        try
        {
            await new Router(blank).RouteAsync(new RouteRequest { Question = "   " }, CancellationToken.None);
        }
        catch (ArgumentException)
        {
            argRefused = true;
        }

        failures = Check(argRefused && blank.Recorded.Count == 0, "a blank question is refused before any call", failures);
        return Summarize(failures, "ERRORS OK");
    }

    private static IEnumerable<string> Keys(JevRequest request, string id) =>
        request.Questions[id].Criteria!.Keys.OrderBy(k => k, StringComparer.Ordinal);
}
