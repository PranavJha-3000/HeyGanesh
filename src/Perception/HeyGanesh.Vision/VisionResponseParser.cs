using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HeyGanesh.Vision;

/// <summary>Recovers a <see cref="VisionSample"/> from the model's completion text, tolerantly.</summary>
/// <remarks>
/// The provider is asked for JSON, but JSON-mode answers are documented to occasionally come back
/// empty and the model may still wrap the object in markdown fences or trail prose. The funnel is:
/// strip fences -> strict JsonDocument parse -> regex extraction from whatever survived. A sample with
/// no recoverable box is marked not-parsed but still carries RawText for diagnostics.
/// </remarks>
internal static class VisionResponseParser
{
    private const int GridMax = 1000;

    private static readonly Regex FoundRegex =
        new(@"\b""?found""?\s*:\s*(true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FieldRegex =
        new(@"\b""?(x1|y1|x2|y2|confidence)""?\s*:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ElementRegex =
        new(@"\b""?(?:element|note)""?\s*:\s*""([^""]*)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static VisionSample Parse(string rawText, TimeSpan latency, int promptTokens, int completionTokens)
    {
        var sample = new VisionSample
        {
            RawText = rawText ?? string.Empty,
            Latency = latency,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
        };

        var text = StripFences(rawText ?? string.Empty);

        // 1. Strict JSON.
        if (TryParseJson(text, out var json, out var error) && json.ValueKind == JsonValueKind.Object)
        {
            sample.Parsed = true;
            ApplyJson(json, sample);
            return sample;
        }

        // 2. Regex fallback against whatever text survived.
        var foundMatch = FoundRegex.Match(text);
        var hasCoords = FieldRegex.Matches(text).Count >= 2;
        if (foundMatch.Success || hasCoords)
        {
            sample.Parsed = true;
            if (foundMatch.Success) sample.Found = bool.Parse(foundMatch.Groups[1].Value);

            foreach (Match m in FieldRegex.Matches(text))
            {
                var field = m.Groups[1].Value.ToLowerInvariant();
                var num = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                switch (field)
                {
                    case "x1": sample.X1 = (int)num; break;
                    case "y1": sample.Y1 = (int)num; break;
                    case "x2": sample.X2 = (int)num; break;
                    case "y2": sample.Y2 = (int)num; break;
                    case "confidence": sample.Confidence = (float)num; break;
                }
            }

            var element = ElementRegex.Match(text);
            if (element.Success) sample.Element = element.Groups[1].Value;

            EnforceRange(sample);
            return sample;
        }

        sample.Parsed = false;
        sample.ParseError = string.IsNullOrWhiteSpace(error) ? "empty or unparseable response" : error;
        return sample;
    }

    private static string StripFences(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```") && text.EndsWith("```") && text.Length > 6)
        {
            var lines = text.Split('\n');
            var first = Array.FindIndex(lines, l => l.TrimStart().StartsWith("```"));
            var last = Array.FindLastIndex(lines, l => l.TrimEnd().EndsWith("```"));
            if (first >= 0 && last > first) text = string.Join('\n', lines[(first + 1)..last]);
        }

        // If prose remains around a JSON object, isolate the outermost { ... }.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

        private static bool TryParseJson(string text, out JsonElement json, out string error)
    {
        json = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty completion (JSON-mode empty-content failure)";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
            json = doc.RootElement.Clone(); // Clone so the reader survives the doc's disposal.
            return true;
        }
        catch (JsonException ex)
        {
            error = $"JSON parse failed: {ex.Message}";
            return false;
        }
    }

    private static void ApplyJson(JsonElement json, VisionSample sample)
    {
        sample.Found = json.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True;

        if (json.TryGetProperty("element", out var el) && el.ValueKind == JsonValueKind.String) sample.Element = el.GetString();
        if (json.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String) sample.Note = n.GetString();

                if (json.TryGetProperty("box", out var box) && box.ValueKind == JsonValueKind.Object)
        {
            sample.X1 = ReadInt(box, "x1"); sample.Y1 = ReadInt(box, "y1");
            sample.X2 = ReadInt(box, "x2"); sample.Y2 = ReadInt(box, "y2");
        }
        // Accept a flat box too (model may ignore nesting).
        if (!sample.X1.HasValue) sample.X1 = ReadInt(json, "x1");
        if (!sample.Y1.HasValue) sample.Y1 = ReadInt(json, "y1");
        if (!sample.X2.HasValue) sample.X2 = ReadInt(json, "x2");
        if (!sample.Y2.HasValue) sample.Y2 = ReadInt(json, "y2");

        sample.Confidence = ReadFloat(json, "confidence");
        EnforceRange(sample);
    }

    private static int? ReadInt(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : null;

    private static float? ReadFloat(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var p) && p.TryGetSingle(out var v) ? v : null;

    private static void EnforceRange(VisionSample sample)
    {
        // The model is instructed to emit 0..1000. Anything outside it (e.g. pixels) is treated as a
        // parse failure rather than silently clamped, so it cannot poison the primary answer.
        var bad = new[] { sample.X1, sample.Y1, sample.X2, sample.Y2 }
            .FirstOrDefault(v => v.HasValue && (v < 0 || v > GridMax));
        if (bad.HasValue)
        {
            sample.Found = false;
            sample.Parsed = false;
            sample.ParseError = $"coordinate out of 0..1000 range (saw {bad})";
        }
    }

}
