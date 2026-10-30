using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.OpenAiCompat;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// The lesson model a live recording uses: the same DeepSeek client the App Shell builds in
/// <c>ShellHost.CreateLessonPlanner</c> (DEC-027/DEC-042). PE-5 reads the shell's source and fails when
/// these constants stop matching it, so a recording always scores the model the student would get.
/// </summary>
/// <remarks>
/// The base URL is a constant on purpose: no command-line flag can point the key at another host (the
/// API Key Isolation invariant). <see cref="Build"/> takes a URL only so PE-4 can aim the real client at its
/// loopback stub with a dummy key; nothing that reads arguments calls it.
/// </remarks>
internal static class LiveClient
{
    public const string BaseUrl = "https://api.deepseek.com";
    public const string ModelId = "deepseek-flash";
    public const string KeyVariable = "DEEPSEEK_API_KEY";
    public const ProviderCapability Capabilities = ProviderCapability.Text | ProviderCapability.Streaming;

    /// <summary>"present (N chars)" or "MISSING": the only way this host ever describes a key.</summary>
    public static string KeyState(string variable = KeyVariable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } k && !string.IsNullOrWhiteSpace(k) ? $"present ({k.Length} chars)" : "MISSING";

    public static ILlmClient Create() => Build(BaseUrl, KeyVariable);

    internal static ILlmClient Build(string baseUrl, string keyVariable) => new OpenAiCompatLlmClient(new OpenAiCompatOptions
    {
        ProviderId = "deepseek",
        BaseUrl = baseUrl,
        ModelId = ModelId,
        Capabilities = Capabilities,
        SecretStore = new EnvKeyStore(keyVariable),
        CredentialProviderId = "deepseek",
    });
}
