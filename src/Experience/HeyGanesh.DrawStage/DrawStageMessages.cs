using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.DrawStage;

// The messages between the app and the vendored page (Page/stage.js), as JSON objects with a "type". Ported from
// openclicky's DrawStageMessages.cs (MIT), trimmed to what Hey Ganesh sends (FEATURE-011 step 4, DEC-071): no photo,
// piece, saving, recording or measure messages, since those features are not offered (DEC-067, DEC-070).

/// <summary>How the page times each beat's strokes: by its own estimate of the line's length (about 2.6 words a second),
/// or by the voice (it waits for lineStarted / lineEnded of each line).</summary>
public enum DrawStagePace
{
    Estimate,
    Speech,
}

/// <summary>One message from the page: its type, the line number (line, lineDue) or line count (programRan), and its text.</summary>
public sealed record DrawStageEvent(string Type, int? K, string? Text);

public static class DrawStageMessages
{
    /// <summary>A new program on a canvas of that size (the screenshot the model saw); what was drawn before is cleared.</summary>
    public static string BeginStream(int canvasWidth, int canvasHeight, DrawStagePace pace) => new JsonObject
    {
        ["type"] = "beginStream",
        ["w"] = Math.Max(1, canvasWidth),
        ["h"] = Math.Max(1, canvasHeight),
        ["pace"] = Pace(pace),
    }.ToJsonString();

    /// <summary>A follow-up's program, run after the one on the stage: the drawing and the program's names stay.</summary>
    public static string ContinueStream(DrawStagePace pace) => new JsonObject { ["type"] = "continueStream", ["pace"] = Pace(pace) }.ToJsonString();

    /// <summary>The program so far (the whole text each time, as a stream grows); <paramref name="done"/> on the last.</summary>
    public static string Feed(string programSoFar, bool done) => new JsonObject { ["type"] = "feed", ["code"] = programSoFar, ["done"] = done }.ToJsonString();

    /// <summary>lineStarted or lineEnded for line <paramref name="k"/>.</summary>
    public static string Line(string type, int k) => new JsonObject { ["type"] = type, ["k"] = k }.ToJsonString();

    /// <summary>A message with only a type: finishNow, clear.</summary>
    public static string Simple(string type) => new JsonObject { ["type"] = type }.ToJsonString();

    /// <summary>A page message; null when it isn't a JSON object with a string "type".</summary>
    public static DrawStageEvent? ParseEvent(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject message || message["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type))
            {
                return null;
            }

            // Line messages carry "k"; programRan carries its count of lines as "lines".
            int? k = (message["k"] ?? message["lines"]) is JsonValue kValue && kValue.TryGetValue<int>(out var number) ? number : null;
            var text = message["text"] is JsonValue textValue && textValue.TryGetValue<string>(out var s) ? s : null;
            return new DrawStageEvent(type, k, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Pace(DrawStagePace pace) => pace == DrawStagePace.Speech ? "speech" : "estimate";
}
