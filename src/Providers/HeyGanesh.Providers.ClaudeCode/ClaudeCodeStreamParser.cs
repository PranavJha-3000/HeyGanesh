// Ported from openclicky windows/src/OpenClicky.Core/Answers/ClaudeCodeAnswerProvider.cs
// (MIT, Copyright (c) 2026 Prasanth Sasikumar and OpenClicky contributors). Adds the result event's usage.

using System.Text;
using System.Text.Json.Nodes;

namespace HeyGanesh.Providers.ClaudeCode;

/// <summary>What the stream said once it ended.</summary>
public sealed record ClaudeCodeStreamSummary
{
    /// <summary>Every <c>text_delta</c> joined, in order.</summary>
    public string StreamedText { get; init; } = "";

    /// <summary>The final <c>result</c> event's text, when it succeeded.</summary>
    public string? ResultText { get; init; }

    /// <summary>Set when the run failed: the result's text (<c>is_error</c>), or an assistant message's error code.</summary>
    public string? FailureText { get; init; }

    /// <summary>Input tokens the result event reported, cache reads and writes included.</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens the result event reported.</summary>
    public int OutputTokens { get; init; }

    /// <summary>
    /// <c>total_cost_usd</c>: what the run would have cost on the API. On a plan nothing is billed per call;
    /// the eval (FEATURE-010 step 3) reports it as the API-equivalent cost.
    /// </summary>
    public double? CostUsd { get; init; }
}

/// <summary>
/// Turns <c>claude -p --output-format stream-json --include-partial-messages</c> output into text deltas and a
/// summary. Fed from the runner's reader thread, so it locks its own state.
/// </summary>
public sealed class ClaudeCodeStreamParser
{
    private readonly object _gate = new();
    private readonly List<byte> _buffered = [];
    private ClaudeCodeStreamSummary _state = new();
    private bool _sawTextDelta;
    private string _assistantText = "";
    private string? _assistantError;

    /// <summary>Consumes a piece of stdout and returns the text deltas from the complete lines in it.</summary>
    public IReadOnlyList<string> Consume(byte[] chunk)
    {
        lock (_gate)
        {
            _buffered.AddRange(chunk);
            var deltas = new List<string>();
            int newline;
            while ((newline = _buffered.IndexOf((byte)'\n')) >= 0)
            {
                var line = _buffered.GetRange(0, newline).ToArray();
                _buffered.RemoveRange(0, newline + 1);
                if (HandleLine(line) is { } delta) deltas.Add(delta);
            }

            return deltas;
        }
    }

    public IReadOnlyList<string> Consume(string text) => Consume(Encoding.UTF8.GetBytes(text));

    /// <summary>Handles a last line with no newline after it.</summary>
    public IReadOnlyList<string> Finish()
    {
        lock (_gate)
        {
            var deltas = new List<string>();
            if (_buffered.Count > 0 && HandleLine(_buffered.ToArray()) is { } delta) deltas.Add(delta);
            _buffered.Clear();
            return deltas;
        }
    }

    public ClaudeCodeStreamSummary Summary
    {
        get
        {
            lock (_gate)
            {
                var s = _state;
                // Without partial messages the whole assistant message is the only copy of the text.
                if (!_sawTextDelta && s.StreamedText.Length == 0) s = s with { StreamedText = _assistantText };
                if (s.FailureText is null && s.ResultText is null && _assistantError is not null)
                {
                    s = s with { FailureText = _assistantText.Length == 0 ? _assistantError : _assistantText };
                }

                return s;
            }
        }
    }

    private string? HandleLine(byte[] line)
    {
        JsonObject? ev;
        try
        {
            ev = JsonNode.Parse(line) as JsonObject;
        }
        catch (Exception)
        {
            return null; // a non-JSON line (a warning) is not part of the answer
        }

        if (ev is null || Str(ev["type"]) is not { } type) return null;
        // Subagent output carries a parent tool id; with no tools there is none, but it is never the answer.
        if (Str(ev["parent_tool_use_id"]) is { Length: > 0 }) return null;

        switch (type)
        {
            case "stream_event":
                if (ev["event"] is not JsonObject inner
                    || Str(inner["type"]) != "content_block_delta"
                    || inner["delta"] is not JsonObject delta
                    || Str(delta["type"]) != "text_delta"
                    || Str(delta["text"]) is not { Length: > 0 } text) return null;
                _sawTextDelta = true;
                _state = _state with { StreamedText = _state.StreamedText + text };
                return text;
            case "assistant":
                if (Str(ev["error"]) is { Length: > 0 } code) _assistantError = code;
                if (ev["message"] is JsonObject message && message["content"] is JsonArray blocks)
                {
                    foreach (var block in blocks.OfType<JsonObject>())
                    {
                        if (Str(block["type"]) == "text") _assistantText += Str(block["text"]) ?? "";
                    }
                }

                return null;
            case "result":
                var resultText = Str(ev["result"]);
                var isError = ev["is_error"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
                var subtype = Str(ev["subtype"]) ?? "";
                var usage = ev["usage"] as JsonObject;
                _state = _state with
                {
                    InputTokens = Int(usage?["input_tokens"]) + Int(usage?["cache_creation_input_tokens"]) + Int(usage?["cache_read_input_tokens"]),
                    OutputTokens = Int(usage?["output_tokens"]),
                    CostUsd = ev["total_cost_usd"] is JsonValue c && c.TryGetValue<double>(out var cost) ? cost : null,
                };
                if (isError || subtype.StartsWith("error", StringComparison.Ordinal))
                {
                    var errors = ev["errors"] is JsonArray arr ? string.Join(" ", arr.Select(e => Str(e)).Where(e => e is not null)) : null;
                    _state = _state with
                    {
                        FailureText = new[] { resultText, errors, subtype }.FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? "unknown error",
                    };
                }
                else
                {
                    _state = _state with { ResultText = resultText };
                }

                return null;
            default:
                return null;
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
}
