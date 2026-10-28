using System.Text.Json;
using System.Text.Json.Nodes;

namespace HeyGanesh.ElementSelection;

// Second half of TypeSafeJevClient: wire-format builders + response parsing.
// (Split across files only to satisfy the editor's per-call size limit; partial by necessity.)

internal sealed partial class TypeSafeJevClient
{
    private static JsonObject BuildQuestions(IReadOnlyDictionary<string, JevQuestion> questions)
    {
        var map = new JsonObject();
        foreach (var (id, question) in questions)
        {
            var obj = new JsonObject
            {
                ["type"] = question.Type,
                ["instructions"] = question.Instructions,
            };

            if (question.Criteria is { } criteria)
            {
                var options = new JsonObject();
                foreach (var (optionId, description) in criteria)
                {
                    options[optionId] = description;
                }

                obj["criteria"] = options;
            }

            map[id] = obj;
        }

        return map;
    }

    private static JevRawResponse ParseResponse(JsonElement root, JevRequest request, TimeSpan latency)
    {
        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
            ? (m.GetString() ?? request.Model)
            : request.Model;

        // Defensive parse: unknown answer shapes surface as null fields rather than thrown-away
        // answers — the service one layer up decides what a missing choice means.
        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        if (root.TryGetProperty("answers", out var answersEl) && answersEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var answer in answersEl.EnumerateObject())
            {
                var v = answer.Value;

                string? type = v.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                string? choice = v.TryGetProperty("choice", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                double? noul = v.TryGetProperty("noul", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : null;
                double? confidence = v.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number ? cf.GetDouble() : null;

                Dictionary<string, double>? probabilities = null;
                if (v.TryGetProperty("probabilities", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                    foreach (var option in p.EnumerateObject())
                    {
                        if (option.Value.ValueKind == JsonValueKind.Number)
                        {
                            probabilities[option.Name] = option.Value.GetDouble();
                        }
                    }
                }

                answers[answer.Name] = new JevAnswer(type, choice, noul, confidence, probabilities);
            }
        }

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = usage.TryGetProperty("input_tokens", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
            outputTokens = usage.TryGetProperty("output_tokens", out var o) && o.TryGetInt32(out var ov) ? ov : 0;
        }

        return new JevRawResponse(model, answers, inputTokens, outputTokens, latency);
    }
}
