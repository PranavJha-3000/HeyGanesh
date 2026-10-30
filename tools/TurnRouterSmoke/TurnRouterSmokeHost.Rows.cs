using HeyGanesh.ElementSelection;
using HeyGanesh.TurnRouter;
using System.Text.RegularExpressions;
using Router = HeyGanesh.TurnRouter.TurnRouter;

namespace HeyGanesh.Tools.TurnRouterSmoke;

internal static partial class TurnRouterSmokeHost
{
    private const string Intent = Router.IntentQuestionId;
    private const string Dimension = Router.DimensionQuestionId;
    private const string Model = Router.ModelQuestionId;

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

    // TR-2: one call carries every question, and each question can stand alone (ids are never shown to Jev).
    private static async Task<int> RunAssertRequestAsync()
    {
        var failures = 0;
        const string Question = "how does a four-stroke engine work?";
        var client = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "lesson", 0.9), (Dimension, "3d", 0.9), (Model, "four-stroke-engine", 0.9)));
        await new Router(client).RouteAsync(Request(Question, found: false), CancellationToken.None);

        failures = Check(client.Recorded.Count == 1, $"exactly one Jev call per turn ({client.Recorded.Count})", failures);
        var req = client.Recorded[0];
        failures = Check(req.Model == "jev-1.13.0", $"model id is the Jev pin ({req.Model})", failures);
        failures = Check(req.Questions.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(new[] { Dimension, Intent, Model }), $"three questions: intent, dimension, model ({string.Join(", ", req.Questions.Keys)})", failures);
        failures = Check(req.Questions.Values.All(q => q.Type == "choice"), "all three are typed choices", failures);
        failures = Check(req.Questions.Values.All(q => q.Instructions.Contains(Question, StringComparison.Ordinal)), "the user's question is quoted in every instruction", failures);
        failures = Check(Keys(req, Intent).SequenceEqual(new[] { "explain", "lesson", "point" }), $"intent options are point / explain / lesson ({string.Join(", ", Keys(req, Intent))})", failures);
        failures = Check(Keys(req, Dimension).SequenceEqual(new[] { "2d", "3d" }), "dimension options are 2d / 3d", failures);
        var expectedModels = Models.Select(m => m.Id).Append(Router.NoModelOptionId).OrderBy(k => k, StringComparer.Ordinal);
        failures = Check(Keys(req, Model).SequenceEqual(expectedModels), $"model options are the offered ids plus none ({string.Join(", ", Keys(req, Model))})", failures);
        failures = Check(req.Questions[Model].Criteria!["dna-helix"] == "DNA double helix (biology)", "each model is described by its title and subject", failures);

        // DEC-051: only a caller that can draw a schematic is told one is made when no listed model fits.
        var drawing = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "lesson", 0.9)));
        await new Router(drawing).RouteAsync(Request(Question) with { Schematics = true }, CancellationToken.None);
        const string Simplified = "simplified 3D drawing";
        failures = Check(
            !req.Questions[Model].Criteria![Router.NoModelOptionId]!.Contains(Simplified, StringComparison.Ordinal)
            && drawing.Recorded[0].Questions[Model].Criteria![Router.NoModelOptionId]!.Contains(Simplified, StringComparison.Ordinal)
            && drawing.Recorded[0].Questions[Dimension].Criteria!["3d"]!.Contains(Simplified, StringComparison.Ordinal),
            "the 3D and 'none' options promise a simplified 3D drawing only when the request allows schematics", failures);

        var state = req.State!.AsObject();
        failures = Check(
            (string?)state["screen_match"] == "not found" && (int?)state["model_count"] == 3 && (int?)state["question_words"] == 6 && (string?)state["window_title"] == "Notes - Physics",
            $"facts computed in code travel as state ({state.ToJsonString()})", failures);
        failures = Check(!state.ToJsonString().Contains(Question, StringComparison.Ordinal), "the question is not repeated in the state", failures);

        var bare = new FakeJevClient(_ => FakeJevClient.Answers((Intent, "explain", 0.9)));
        await new Router(bare).RouteAsync(Request("what is entropy?", withModels: false), CancellationToken.None);
        failures = Check(bare.Recorded[0].Questions.Keys.SequenceEqual(new[] { Intent }), "with no models only the intent question is asked", failures);
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

        var point = await RouteWith(r, (Intent, "point", 0.9), (Dimension, "3d", 0.9), (Model, "dna-helix", 0.9));
        failures = Check(point.Routed && point.Intent == TurnIntent.Point && point.Dimension is null && point.ModelId is null, $"point -> point, the 3D answers ignored ({point})", failures);

        var explain = await RouteWith(r, (Intent, "explain", 0.9));
        failures = Check(explain.Routed && explain.Intent == TurnIntent.Explain && explain.Dimension is null, $"explain -> explain ({explain})", failures);

        var scene = await RouteWith(r, (Intent, "lesson", 0.9), (Dimension, "3d", 0.8), (Model, "four-stroke-engine", 0.7));
        failures = Check(scene.Routed && scene.Dimension == LessonDimension.Scene3d && scene.ModelId == "four-stroke-engine" && scene.Reason is null, $"lesson + 3d + engine -> 3D lesson on the engine ({scene})", failures);
        failures = Check(scene.IntentScore == 0.9 && scene.DimensionScore == 0.8 && scene.ModelScore == 0.7, "each score is reported", failures);
        failures = Check(scene.JevModel == "jev-1.13.0" && scene.InputTokens == 420, "the provider's model and token counts are carried", failures);

        var flat = await RouteWith(r, (Intent, "lesson", 0.9), (Dimension, "2d", 0.9), (Model, "dna-helix", 0.9));
        failures = Check(flat.Routed && flat.Dimension == LessonDimension.Flat && flat.ModelId is null, $"lesson + 2d -> 2D lesson, no model even if one was named ({flat})", failures);

        var none = await RouteWith(r, (Intent, "lesson", 0.9), (Dimension, "3d", 0.9), (Model, "none", 0.9));
        failures = Check(none.Routed && none.Dimension == LessonDimension.Flat && !none.Schematic && none.Reason is not null, $"lesson + 3d + none -> 2D lesson with a reason, when schematics are not allowed ({none.Reason})", failures);

        // DEC-051: a caller that can draw a schematic gets one instead, gated like any model answer.
        var drawn = r with { Schematics = true };
        var schematic = await RouteWith(drawn, (Intent, "lesson", 0.9), (Dimension, "3d", 0.9), (Model, "none", 0.8));
        failures = Check(schematic.Routed && schematic.Dimension == LessonDimension.Scene3d && schematic.Schematic && schematic.ModelId is null && schematic.ModelScore == 0.8,
            $"with schematics allowed, lesson + 3d + none -> 3D schematic lesson, no model id ({schematic}; {schematic.Reason})", failures);
        var unsureNone = await RouteWith(drawn, (Intent, "lesson", 0.9), (Dimension, "3d", 0.9), (Model, "none", 0.3));
        failures = Check(unsureNone.Routed && unsureNone.Dimension == LessonDimension.Flat && !unsureNone.Schematic && unsureNone.Reason!.Contains("0.30", StringComparison.Ordinal),
            $"an unsure 'none' (0.30) is still a 2D lesson ({unsureNone.Reason})", failures);

        var noModels = await RouteWith(Request("derive the chain rule", withModels: false), (Intent, "lesson", 0.9));
        failures = Check(noModels.Routed && noModels.Dimension == LessonDimension.Flat, "lesson with no catalogue -> 2D lesson", failures);

        var badIntent = await RouteWith(r, (Intent, "draw", 0.99));
        failures = Check(!badIntent.Routed && badIntent.Reason!.Contains("never offered", StringComparison.Ordinal), $"an intent never offered refuses the route ({badIntent.Reason})", failures);

        var badModel = await RouteWith(r, (Intent, "lesson", 0.99), (Dimension, "3d", 0.99), (Model, "jet-engine", 0.99));
        failures = Check(!badModel.Routed && badModel.ModelId is null, $"a model never offered refuses the route ({badModel.Reason})", failures);

        var badDim = await RouteWith(r, (Intent, "lesson", 0.99), (Dimension, "4d", 0.99), (Model, "dna-helix", 0.99));
        failures = Check(!badDim.Routed, $"a dimension never offered refuses the route ({badDim.Reason})", failures);

        var missing = await RouteWith(r);
        failures = Check(!missing.Routed && missing.Reason!.Contains("no answer", StringComparison.Ordinal), $"no intent answer -> no route ({missing.Reason})", failures);

        var refused = false;
        try
        {
            await RouteWith(new RouteRequest { Question = "x", Models = new[] { new RouteModel("none", "Nothing", "none") } }, (Intent, "point", 0.9));
        }
        catch (ArgumentException)
        {
            refused = true;
        }

        failures = Check(refused, "a catalogue model with the reserved id 'none' is refused as a caller bug", failures);
        return Summarize(failures, "MAP OK");
    }

    // TR-4: the 0.5 gate. Unsure about the intent -> no route; unsure only about the canvas -> still a lesson, in 2D.
    private static async Task<int> RunAssertGateAsync()
    {
        var failures = 0;
        var r = Request("teach me");

        var low = await RouteWith(r, (Intent, "lesson", 0.49), (Dimension, "3d", 0.9), (Model, "dna-helix", 0.9));
        failures = Check(!low.Routed && low.Intent is null && low.Reason!.Contains("below 0.50", StringComparison.Ordinal), $"intent at 0.49 -> no route ({low.Reason})", failures);

        var edge = await RouteWith(r, (Intent, "point", 0.5));
        failures = Check(edge.Routed && edge.Intent == TurnIntent.Point, "intent at exactly 0.50 is acted on (the Medium tier boundary)", failures);

        var dimLow = await RouteWith(r, (Intent, "lesson", 0.9), (Dimension, "3d", 0.3), (Model, "dna-helix", 0.9));
        failures = Check(dimLow.Routed && dimLow.Dimension == LessonDimension.Flat && dimLow.ModelId is null && dimLow.Reason!.Contains("2D", StringComparison.Ordinal), $"3D at 0.30 -> 2D lesson ({dimLow.Reason})", failures);

        var modelLow = await RouteWith(r, (Intent, "lesson", 0.9), (Dimension, "3d", 0.9), (Model, "dna-helix", 0.2));
        failures = Check(modelLow.Routed && modelLow.Dimension == LessonDimension.Flat && modelLow.ModelId is null, $"model at 0.20 -> 2D lesson ({modelLow.Reason})", failures);

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
