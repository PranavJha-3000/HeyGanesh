using HeyGanesh.TurnRouter;

namespace HeyGanesh.Tools.TurnRouterSmoke;

internal static partial class TurnRouterSmokeHost
{
    // TR-6: what the App Shell runs for every flag and route (DEC-036; intent only since DEC-075). The shell only applies this answer.
    private static int RunAssertLegs()
    {
        var failures = 0;
        var point = new TurnRoute { Routed = true, Intent = TurnIntent.Point };
        var explain = new TurnRoute { Routed = true, Intent = TurnIntent.Explain };
        var lesson = new TurnRoute { Routed = true, Intent = TurnIntent.Lesson };
        var refused = new TurnRoute { Routed = false, Reason = "intent 'lesson' scored 0.40, below 0.50." };

        var forced = TurnLegs.Choose(point, forceLesson: true, noRoute: true);
        failures = Check(forced.Leg == TurnLeg.Lesson, "--lesson -> lesson, whatever the route and --no-route say", failures);

        var off = TurnLegs.Choose(lesson, forceLesson: false, noRoute: true);
        failures = Check(off.Leg == TurnLeg.Tutor && off.Why.Contains("--no-route", StringComparison.Ordinal), "--no-route -> the Tutor leg, the route ignored", failures);

        var none = TurnLegs.Choose(null, false, false);
        failures = Check(none.Leg == TurnLeg.Tutor, "no router (or a failed one) -> the Tutor leg", failures);

        var notRouted = TurnLegs.Choose(refused, false, false);
        failures = Check(notRouted.Leg == TurnLeg.Tutor && notRouted.Why.Contains("0.40", StringComparison.Ordinal), $"no route -> the Tutor leg, with the router's reason ({notRouted.Why})", failures);

        failures = Check(TurnLegs.Choose(point, false, false).Leg == TurnLeg.Highlight, "point -> highlight only", failures);
        failures = Check(TurnLegs.Choose(explain, false, false).Leg == TurnLeg.Tutor, "explain -> the Tutor leg", failures);
        failures = Check(TurnLegs.Choose(lesson, false, false) is { Leg: TurnLeg.Lesson, Why: "routed: lesson" }, "lesson -> the lesson leg (the drawing picks 2D or 3D)", failures);
        return Summarize(failures, "LEGS OK");
    }
}
