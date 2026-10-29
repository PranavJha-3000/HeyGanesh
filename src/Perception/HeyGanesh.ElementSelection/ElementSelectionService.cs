using System.Diagnostics;
using System.Runtime.InteropServices;
using HeyGanesh.Abstractions;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// Public API of the Element Selection module — the PRIMARY coordinate-detection path (DEC-008):
/// enumerate the UIA tree of the target window, cap it to an interactive candidate manifest, and let
/// Jev select the best match for the user's question. Geometry is exact (UIA BoundingRectangle);
/// Jev only ever selects among the offered candidates — it can never emit coordinates or free text.
/// </summary>
/// <remarks>
/// Threshold-free by contract (same as Vision): every raw signal (choice, probabilities, noul
/// plausibility, confidence) is surfaced and nothing is thresholded here. A future Jev
/// confidence-gate decides when to fall back to the Vision module; the composition contract is
/// documented in DEC-008 and ARCHITECTURE.md. Policy that lives here: candidate filtering/capping,
/// option-id assignment, the "none" escape hatch, and the hallucination guard on unmapped choice ids.
/// Transport is entirely behind <see cref="IJevClient"/> (DEC-008).
/// </remarks>
public sealed partial class ElementSelectionService : IDisposable
{
    private const string TargetQuestionId = "target";
    private const string PlausibleQuestionId = "plausible";
    private const string NoneOptionId = "none";

    private readonly IJevClient _client;
    private readonly SelectionOptions _options;
    private readonly CandidateEnumerator _enumerator;
    private readonly bool _ownsClient;

    /// <summary>
    /// Creates a service. If <paramref name="client"/> is null a <see cref="TypeSafeJevClient"/>
    /// (reading <c>TYPESAFE_API_KEY</c> from the environment) is used; pass another
    /// <see cref="IJevClient"/> to redirect to a proxy or a test double.
    /// </summary>
    public ElementSelectionService(IJevClient? client = null, SelectionOptions? options = null)
    {
        _options = options ?? SelectionOptions.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxCandidates, 1);

        if (client is null)
        {
            _client = new TypeSafeJevClient(_options);
            _ownsClient = true;
        }
        else
        {
            _client = client;
        }

        _enumerator = new CandidateEnumerator(_options);
    }

    /// <summary>
    /// Selects the UI element best matching <paramref name="question"/> in the target window.
    /// Enumeration failures (no window, UIA access denied) come back as a Failed
    /// <see cref="SelectionResult"/> rather than an exception — they are the Vision fallback
    /// trigger, not errors; provider auth/HTTP failures throw, like Vision's do.
    /// </summary>
    public Task<SelectionResult> SelectAsync(
        string question,
        SelectionTarget? target = null,
        CancellationToken ct = default)
        => SelectAsync(question, target, Array.Empty<ScreenTextLine>(), ct);

    /// <summary>
    /// As above, also offering Jev the screen text the capture read (DEC-039): lines UI Automation
    /// cannot see, filtered by <see cref="TextCandidates"/> (inside the window, not on a control,
    /// short, no secrets, at most 40) and ranked with the UIA candidates under the same cap.
    /// </summary>
    public async Task<SelectionResult> SelectAsync(
        string question,
        SelectionTarget? target,
        IReadOnlyList<ScreenTextLine> screenText,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(screenText);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var started = Stopwatch.GetTimestamp();

        CandidateSet set;
        try
        {
            set = _enumerator.Enumerate(target);
        }
        catch (COMException ex)
        {
            return new SelectionResult
            {
                Found = false,
                FailureReason = $"UIA enumeration failed: {ex.Message}",
                ModelId = _options.ModelId,
                Latency = Stopwatch.GetElapsedTime(started),
            };
        }
        catch (InvalidOperationException ex)
        {
            return new SelectionResult
            {
                Found = false,
                FailureReason = $"UIA enumeration unavailable: {ex.Message}",
                ModelId = _options.ModelId,
                Latency = Stopwatch.GetElapsedTime(started),
            };
        }

        // Cap before Jev: rank by cheap lexical overlap, take the top MaxCandidates. Jev never sees
        // the raw tree (state budget) and sees ids in ranked order (c001 = best lexical guess).
        var offered = screenText.Count == 0
            ? set.Candidates
            : set.Candidates.Concat(TextCandidates.FromScreenText(screenText, set.Candidates, set.Target.WindowRect)).ToList();
        var ranked = CandidateRanker.RankAndCap(offered, question, _options.MaxCandidates);
        var manifest = new List<UiaCandidate>(ranked.Count);
        for (int i = 0; i < ranked.Count; i++)
        {
            manifest.Add(ranked[i] with { Id = $"c{(i + 1):000}" });
        }

        if (manifest.Count == 0)
        {
            // The documented primary fallback trigger: the UIA tree yielded no named, visible,
            // enabled interactive elements (elevated / custom-rendered host). Defer to Vision (DEC-008).
            return new SelectionResult
            {
                Found = false,
                Target = set.Target,
                TotalEnumerated = set.TotalEnumerated,
                TotalAfterFilter = set.InteractiveAfterFilter,
                Candidates = manifest,
                ModelId = _options.ModelId,
                Latency = Stopwatch.GetElapsedTime(started),
                FailureReason = "no interactive candidates: the UIA tree of the target window yielded " +
                                "no named, visible, enabled interactive elements (elevated or " +
                                "custom-rendered host?) and no usable screen text. Defer to Vision.",
            };
        }

        var request = BuildRequest(question, set.Target, manifest);
        var response = await _client.EvaluateAsync(request, ct).ConfigureAwait(false);

        return MapResponse(response, set, manifest, Stopwatch.GetElapsedTime(started));
    }

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable d)
        {
            d.Dispose();
        }
    }
}
