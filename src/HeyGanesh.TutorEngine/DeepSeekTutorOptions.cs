namespace HeyGanesh.TutorEngine;

/// <summary>
/// Operating policy for the first live teaching provider: DeepSeek, direct API, OpenAI-compatible
/// Chat Completions shape. Mirrors <c>VisionOptions</c>' role — where to talk to, which model, how
/// the optional image is processed, how long one call may take. (See DEC-017; transport conventions
/// conceptually shared with the Vision leg, no code shared — DEC-009.)
/// </summary>
/// <remarks>
/// No credentials live here and none can: the key is resolved from <c>DEEPSEEK_API_KEY</c> at call
/// time inside the client (DEC-007's temporary direct-key stopgap, extended through a second seam).
///
/// <b>What is deliberately absent:</b> thinking/reasoning, temperature, top-p, response format and
/// streaming knobs. The teaching contract (<see cref="TutorPrompt"/>) carries no sampling or reasoning
/// policy, so exposing them here would invent provider policy the contract never asked for. One
/// provider decision IS fixed here rather than exposed: DeepSeek's <c>deepseek-flash</c> currently
/// defaults to thinking enabled at high effort, and this tutor sends
/// <c>thinking: {"type": "disabled"}</c> — the answer is meant to be spoken verbatim, so predictable
/// latency and an output budget that bounds the lesson itself (not a reasoning trace) win over
/// provider-side deliberation. That is a fixed v1 decision recorded in DEC-017, revisited only with
/// measured evidence, which is exactly why it is not a property on this type.
/// </remarks>
public sealed class DeepSeekTutorOptions
{
    /// <summary>Defaults: <c>deepseek-flash</c> @ api.deepseek.com, high image detail, 60 s per call.</summary>
    public static DeepSeekTutorOptions Default { get; } = new();

    /// <summary>Model identifier sent in the Chat Completions request. <c>deepseek-flash</c> is the current name (V4.1-Flash, the vision-capable one).</summary>
    public string ModelId { get; init; } = "deepseek-flash";

    /// <summary>
    /// Chat Completions base URL. Override to route through a local API key security proxy — the
    /// same redirect seam Vision uses — and, for offline verification, to point the exact same
    /// client at a loopback stub (TU-11). Production default never changes.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.deepseek.com";

    /// <summary>Provider image fidelity flag forwarded as <c>image_url.detail</c> (low | high | original | auto). High keeps the original resolution — teaching legibility wins over token cost.</summary>
    public string ImageDetail { get; init; } = "high";

    /// <summary>How long a single provider call may take before it is abandoned (same bound family as Vision's).</summary>
    public TimeSpan PerCallTimeout { get; init; } = TimeSpan.FromSeconds(60);
}