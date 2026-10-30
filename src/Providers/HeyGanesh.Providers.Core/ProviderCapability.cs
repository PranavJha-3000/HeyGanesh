namespace HeyGanesh.Providers.Core;

/// <summary>
/// Capability flags describing what a provider or model can do. A request requiring a
/// capability the selected model lacks fails fast with <see cref="ProviderErrorKind.CapabilityMissing"/>
/// (never a silent downgrade).
/// </summary>
[Flags]
public enum ProviderCapability
{
    None = 0,
    Text = 1 << 0,
    Vision = 1 << 1,
    StructuredOutput = 1 << 2,
    Streaming = 1 << 3,
    ToolCalls = 1 << 4,
    Embeddings = 1 << 5,
    Stt = 1 << 6,
    Tts = 1 << 7,
}

/// <summary>
/// The role a provider+model is assigned to. Mix-and-match across roles is first-class.
/// </summary>
public enum ProviderRole
{
    Tutor = 0,
    Vision = 1,
    Embedder = 2,
    Stt = 3,
    Tts = 4,
    Jev = 5,
}