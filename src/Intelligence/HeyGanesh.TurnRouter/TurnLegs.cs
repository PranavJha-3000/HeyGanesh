namespace HeyGanesh.TurnRouter;

/// <summary>Which teaching leg a turn runs after its verdict.</summary>
public enum TurnLeg
{
    /// <summary>Nothing more: the highlight already on screen was the answer.</summary>
    Highlight,

    /// <summary>The Tutor's explanation (today's default leg).</summary>
    Tutor,

    /// <summary>A spoken drawing on the drawing stage; the program chooses 2D or 3D (DEC-075).</summary>
    Lesson,
}

/// <summary>The leg to run and why, in words the shell prints as-is.</summary>
public sealed record LegChoice(TurnLeg Leg, string Why);

/// <summary>
/// Turns the shell's flags and a route into the leg to run (DEC-036). Pure, so every case is checked
/// offline (TR-6); the App Shell, which carries no fakes (DEC-012), only applies the answer.
/// </summary>
public static class TurnLegs
{
    /// <param name="route">The router's verdict, or null when no router ran or it failed.</param>
    /// <param name="forceLesson"><c>--lesson</c>: always a lesson, the router never asked.</param>
    /// <param name="noRoute"><c>--no-route</c>: today's default leg, the router never asked.</param>
    public static LegChoice Choose(TurnRoute? route, bool forceLesson, bool noRoute)
    {
        // The flags come first and keep exactly the behaviour they had before the router existed.
        if (forceLesson)
        {
            return new(TurnLeg.Lesson, "--lesson forces a lesson");
        }

        if (noRoute)
        {
            return new(TurnLeg.Tutor, "--no-route: the Tutor leg, as without a router");
        }

        if (route is null)
        {
            return new(TurnLeg.Tutor, "no route was made: the Tutor leg, as without a router");
        }

        if (!route.Routed)
        {
            return new(TurnLeg.Tutor, $"no route ({route.Reason}): the Tutor leg, as without a router");
        }

        return route.Intent switch
        {
            TurnIntent.Point => new(TurnLeg.Highlight, "routed: point, the highlight was the answer"),
            TurnIntent.Explain => new(TurnLeg.Tutor, "routed: explain"),
            TurnIntent.Lesson => new(TurnLeg.Lesson, "routed: lesson"),
            _ => new(TurnLeg.Tutor, "unknown intent: the Tutor leg, as without a router"),
        };
    }
}
