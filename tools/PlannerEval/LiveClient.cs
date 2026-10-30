using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.OpenAiCompat;
using HeyGanesh.Security;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The lesson model a live recording uses: the same DeepSeek client the App Shell builds in
/// <c>ShellHost.CreateLessonPlanner</c> (DEC-027/DEC-042), reading its key through the same key ring
/// (DEC-046: Credential Manager, then <c>DEEPSEEK_API_KEY</c>). PE-5 reads the shell's source and fails when
/// these constants stop matching it, so a recording always scores the model the student would get.
/// </summary>
/// <remarks>
/// The base URL is a constant on purpose: no command-line flag can point the key at another host (the
/// API Key Isolation invariant). <see cref="Build"/> takes a URL only so PE-4 can aim the real client at its
/// loopback stub with a ring holding a dummy key; nothing that reads arguments calls it.
/// </remarks>
internal static class LiveClient
{
    public const string BaseUrl = "https://api.deepseek.com";
    public const string ModelId = "deepseek-flash";
    public const string KeyName = "deepseek";
    public const ProviderCapability Capabilities = ProviderCapability.Text | ProviderCapability.Streaming;

    public static ILlmClient Create(KeyRing ring) => Build(BaseUrl, ring);

    internal static ILlmClient Build(string baseUrl, KeyRing ring) => new OpenAiCompatLlmClient(new OpenAiCompatOptions
    {
        ProviderId = "deepseek",
        BaseUrl = baseUrl,
        ModelId = ModelId,
        Capabilities = Capabilities,
        SecretStore = new RingKeyStore(ring, KeyName),
        CredentialProviderId = "deepseek",
    });
}
