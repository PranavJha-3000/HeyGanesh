using HeyGanesh.TurnRouter;

namespace HeyGanesh.Tools.TurnRouterSmoke;

internal static partial class TurnRouterSmokeHost
{
    // TR-6: what the App Shell runs for every flag and route (DEC-036). The shell only applies this answer.
    private static int RunAssertLegs()
    {
        var failures = 0;
        var point = new TurnRoute { Routed = true, Intent = TurnIntent.Point };
        var explain = new TurnRoute { Routed = true, Intent = TurnIntent.Explain };
        var flat = new TurnRoute { Routed = true, Intent = TurnIntent.Lesson, Dimension = LessonDimension.Flat };
        var unsure = flat with { Reason = "3D scored 0.30, below 0.50; drawing in 2D." };
        var scene = new TurnRoute { Routed = true, Intent = TurnIntent.Lesson, Dimension = LessonDimension.Scene3d, ModelId = "dna-helix" };
        var refused = new TurnRoute { Routed = false, Reason = "intent 'lesson' scored 0.40, below 0.50." };

        var forced = TurnLegs.Choose(point, forceLesson: true, noRoute: true);
        failures = Check(forced is { Leg: TurnLeg.Lesson, Canvas: LessonCanvas.FullCatalogue, ModelId: null }, "--lesson -> lesson on the full catalogue, whatever the route and --no-route say", failures);

        var off = TurnLegs.Choose(scene, forceLesson: false, noRoute: true);
        failures = Check(off is { Leg: TurnLeg.Tutor, Canvas: LessonCanvas.None } && off.Why.Contains("--no-route", StringComparison.Ordinal), "--no-route -> the Tutor leg, the route ignored", failures);

        var none = TurnLegs.Choose(null, false, false);
        failures = Check(none.Leg == TurnLeg.Tutor, "no router (or a failed one) -> the Tutor leg", failures);

        var notRouted = TurnLegs.Choose(refused, false, false);
        failures = Check(notRouted.Leg == TurnLeg.Tutor && notRouted.Why.Contains("0.40", StringComparison.Ordinal), $"no route -> the Tutor leg, with the router's reason ({notRouted.Why})", failures);

        failures = Check(TurnLegs.Choose(point, false, false) is { Leg: TurnLeg.Highlight, Canvas: LessonCanvas.None }, "point -> highlight only", failures);
        failures = Check(TurnLegs.Choose(explain, false, false) is { Leg: TurnLeg.Tutor, Canvas: LessonCanvas.None }, "explain -> the Tutor leg", failures);
        failures = Check(TurnLegs.Choose(flat, false, false) is { Leg: TurnLeg.Lesson, Canvas: LessonCanvas.Flat, ModelId: null }, "2D lesson -> lesson with no 3D offered", failures);

        var downgraded = TurnLegs.Choose(unsure, false, false);
        failures = Check(downgraded.Canvas == LessonCanvas.Flat && downgraded.Why.Contains("0.30", StringComparison.Ordinal), $"a 3D downgrade keeps its reason ({downgraded.Why})", failures);

        failures = Check(TurnLegs.Choose(scene, false, false) is { Leg: TurnLeg.Lesson, Canvas: LessonCanvas.OneModel, ModelId: "dna-helix" }, "3D lesson -> lesson offered that one model", failures);

        var noModel = TurnLegs.Choose(scene with { ModelId = null }, false, false);
        failures = Check(noModel.Canvas == LessonCanvas.Flat, "a 3D route with no model id is a 2D lesson, never the full catalogue", failures);
        return Summarize(failures, "LEGS OK");
    }
}
