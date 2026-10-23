namespace HeyGanesh.TutorEngine;

/// <summary>
/// The one public way to construct a live teaching provider. (DEC-017.)
/// </summary>
/// <remarks>
/// <para>
/// The concrete clients are <c>internal</c> on purpose: consumers depend on <see cref="ITutorClient"/>
/// plus this one construction method, never on a provider type name — which is what keeps DEC-015's
/// "swapping providers touches construction only" true at the API level, not just by convention.
/// </para>
/// <para>
/// <b>This type is construction, nothing else.</b> No teaching logic, no key handling (the seam
/// resolves its own key at call time, DEC-007), no provider enum, no registry. A second provider gets
/// its own method only via a new DECISIONS entry — not by accretion. The App Shell's teaching leg was
/// wired behind exactly this call (DEC-016 left <c>CreateTutorClient()</c> null until the provider
/// decision; the flip in the shell will be <c>TutorClientFactory.CreateDeepSeek()</c> and nothing more).
/// </para>
/// </remarks>
public static class TutorClientFactory
{
    /// <summary>
    /// Creates the DeepSeek teaching client (model <c>deepseek-flash</c> unless the options say
    /// otherwise), returned as the seam interface so callers cannot depend on the concrete type.
    /// The returned instance is caller-owned and disposable — <see cref="TutorService"/> will not
    /// dispose it.
    /// </summary>
    public static ITutorClient CreateDeepSeek(DeepSeekTutorOptions? options = null) =>
        new DeepSeekTutorClient(options);
}