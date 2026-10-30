namespace HeyGanesh.TurnRouter;

/// <summary>Which teaching leg a turn runs after its verdict.</summary>
public enum TurnLeg
{
    /// <summary>Nothing more: the highlight already on screen was the answer.</summary>
    Highlight,

    /// <summary>The Tutor's explanation (today's default leg).</summary>
    Tutor,

    /// <summary>A drawn, spoken lesson.</summary>
    Lesson,
}

/// <summary>Which 3D models a lesson's planner is offered.</summary>
public enum LessonCanvas
{
    /// <summary>Not a lesson.</summary>
    None,

    /// <summary>2D cards only: the planner is offered no 3D.</summary>
    Flat,

    /// <summary>Only <see cref="LegChoice.ModelId"/>.</summary>
    OneModel,

    /// <summary>The whole catalogue (what <c>--lesson</c> has always offered).</summary>
    FullCatalogue,

    /// <summary>
    /// 3D with no listed model: the lesson draws its own simplified one (DEC-051). The shell offers the
    /// whole catalogue too, so the planner can still use a library model if one turns out to fit.
    /// </summary>
    Schematic,
}

/// <summary>The leg to run and why, in words the shell prints as-is.</summary>
public sealed record LegChoice(TurnLeg Leg, LessonCanvas Canvas, string? ModelId, string Why);

/// <summary>
/// Turns the shell's flags and a route into the leg to run (DEC-036). Pure, so every case is checked
/// offline (TR-6); the App Shell, which carries no fakes (DEC-012), only applies the answer.
/// </summary>
public static class TurnLegs
{
    /// <param name="route">The router's verdict, or null when no router ran or it failed.</param>
    /// <param name="forceLesson"><c>--lesson</c>: always a lesson on the full catalogue, the router never asked.</param>
    /// <param name="noRoute"><c>--no-route</c>: today's default leg, the router never asked.</param>
    public static LegChoice Choose(TurnRoute? route, bool forceLesson, bool noRoute)
    {
        // The flags come first and keep exactly the behaviour they had before the router existed.
        if (forceLesson)
        {
            return new(TurnLeg.Lesson, LessonCanvas.FullCatalogue, null, "--lesson forces a lesson");
        }

        if (noRoute)
        {
            return new(TurnLeg.Tutor, LessonCanvas.None, null, "--no-route: the Tutor leg, as without a router");
        }

        if (route is null)
        {
            return new(TurnLeg.Tutor, LessonCanvas.None, null, "no route was made: the Tutor leg, as without a router");
        }

        if (!route.Routed)
        {
            return new(TurnLeg.Tutor, LessonCanvas.None, null, $"no route ({route.Reason}): the Tutor leg, as without a router");
        }

        return route.Intent switch
        {
            TurnIntent.Point => new(TurnLeg.Highlight, LessonCanvas.None, null, "routed: point, the highlight was the answer"),
            TurnIntent.Explain => new(TurnLeg.Tutor, LessonCanvas.None, null, "routed: explain"),
            TurnIntent.Lesson when route.Dimension == LessonDimension.Scene3d && route.ModelId is { } model =>
                new(TurnLeg.Lesson, LessonCanvas.OneModel, model, $"routed: 3D lesson on {model}"),
            TurnIntent.Lesson when route.Dimension == LessonDimension.Scene3d && route.Schematic =>
                new(TurnLeg.Lesson, LessonCanvas.Schematic, null, "routed: 3D lesson with a schematic, no listed model fits"),
            TurnIntent.Lesson => new(TurnLeg.Lesson, LessonCanvas.Flat, null, route.Reason is { } why ? $"routed: 2D lesson ({why})" : "routed: 2D lesson"),
            _ => new(TurnLeg.Tutor, LessonCanvas.None, null, "unknown intent: the Tutor leg, as without a router"),
        };
    }
}
