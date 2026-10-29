using HeyGanesh.Abstractions;
using HeyGanesh.ElementSelection;
using HeyGanesh.PartLibrary;
using HeyGanesh.TurnRouter;
using HeyGanesh.VisualLessonEngine;
using Router = HeyGanesh.TurnRouter.TurnRouter;

namespace HeyGanesh.App;

// The routing leg (DEC-036): after a produced verdict, one Jev call decides whether the turn is
// pointed at, explained or taught, and on which canvas. The decision itself is TurnLegs.Choose, a pure
// function TurnRouterSmoke checks case by case (TR-6); this file only builds the request, survives a
// failed router, and prints what was decided. A router fault never fails the turn: the verdict was
// already answered, so the turn falls back to the leg it would have run without a router.

internal static partial class ShellHost
{
    private const string RouterKeyEnvVar = "TYPESAFE_API_KEY";

    /// <summary>
    /// The router is composed only when it can change something: not with --lesson (which forces the
    /// lesson) or --no-route, and not without the Jev key (Element Selection would stop the turn at
    /// the key gate first anyway). It shares the shell's one Jev client with Element Selection.
    /// </summary>
    private static Router? CreateRouter(ShellOptions options, IJevClient jev) =>
        options.Lesson || options.NoRoute || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RouterKeyEnvVar))
            ? null
            : new Router(jev);

    /// <summary>The 3D models the router may offer: the Part Library's, by title and subject.</summary>
    private static IReadOnlyList<RouteModel> RouteModels { get; } =
        ModelLibrary.V1.Select(m => new RouteModel(m.Id, m.Title, m.Subject)).ToArray();

    /// <summary>
    /// Starts the route as soon as the verdict is known, so it runs while the highlight is on screen.
    /// Null when there is no router or the verdict failed (nothing is taught from a failed verdict).
    /// The task never faults: a provider error or a bad request is printed and becomes a null route;
    /// cancellation becomes a null route too, and TeachAsync reports it from the token.
    /// </summary>
    private static Task<TurnRoute?>? StartRoute(Router? router, ShellOptions options, GuidanceResult guidance, CancellationToken ct)
    {
        if (router is null || guidance.Failed)
        {
            return null;
        }

        return RouteSafelyAsync(router, new RouteRequest
        {
            Question = options.Question,
            ScreenMatchFound = guidance.Found,
            Models = RouteModels,
        }, ct);
    }

    private static async Task<TurnRoute?> RouteSafelyAsync(Router router, RouteRequest request, CancellationToken ct)
    {
        try
        {
            return await router.RouteAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (JevClientException ex)
        {
            // Auth, rate limit and HTTP faults alike: the exception message carries no key (TypeSafeJevClient).
            Console.WriteLine($"route       : FAILED ({ex.GetType().Name}: {ex.Message}); teaching as without a router");
            return null;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"route       : FAILED (bad request: {ex.Message}); teaching as without a router");
            return null;
        }
    }

    /// <summary>
    /// Awaits the route (if one was started), prints it, and returns the leg to run. Null means the
    /// token was cancelled while routing: the caller exits 13 without teaching.
    /// </summary>
    private static async Task<LegChoice?> ChooseLegAsync(Task<TurnRoute?>? routing, ShellOptions options, CancellationToken ct)
    {
        TurnRoute? route = null;
        if (routing is not null)
        {
            route = await routing.ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            if (route is not null)
            {
                Console.WriteLine($"route       : {route}  intent={Score(route.IntentScore)} 3d={Score(route.DimensionScore)} " +
                                  $"model={Score(route.ModelScore)}  jev={route.JevModel}  latency={route.Latency.TotalMilliseconds:F0} ms");
            }
        }

        var leg = TurnLegs.Choose(route, options.Lesson, options.NoRoute);
        if (routing is not null)
        {
            Console.WriteLine($"  leg         : {leg.Leg} — {leg.Why}");
        }

        return leg;
    }

    /// <summary>The planner's 3D offer for a leg: none (2D), one model, or the whole catalogue (--lesson).</summary>
    private static ModelCatalog? CatalogFor(LegChoice leg) => leg.Canvas switch
    {
        LessonCanvas.FullCatalogue => LessonSurface.CardSurface.Scene3dCatalog,
        LessonCanvas.OneModel => new ModelCatalog(LessonSurface.CardSurface.Scene3dCatalog.Models.Where(m => m.Id == leg.ModelId)),
        _ => null,
    };

    private static string Score(double? score) => score is { } s ? s.ToString("0.00") : "-";

    /// <summary>Whether CreateRouter() will compose a router: the header says so before any turn runs.</summary>
    private static bool RoutesLessons(ShellOptions options, bool typesafeKey) => !options.Lesson && !options.NoRoute && typesafeKey;

    /// <summary>The header's route line; keep in step with CreateRouter().</summary>
    private static string RouteHeader(ShellOptions options, bool typesafeKey) =>
        options.Lesson ? "off (--lesson forces a lesson)"
        : options.NoRoute ? "off (--no-route: the Tutor leg, as before the router)"
        : typesafeKey ? $"ON: Jev chooses point / explain / lesson, 2D or 3D, over {RouteModels.Count} models (TYPESAFE_API_KEY present, value suppressed)"
        : "off (TYPESAFE_API_KEY MISSING)";
}
