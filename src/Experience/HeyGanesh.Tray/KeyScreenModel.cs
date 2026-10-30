using HeyGanesh.Security;

namespace HeyGanesh.Tray;

/// <summary>
/// What the key screen says and does, with no UI (DEC-053), so every rule is checked offline (TK-1).
/// </summary>
/// <remarks>
/// Every key goes through the one <see cref="KeyRing"/> (DEC-045): Save is <see cref="KeyRing.Store"/>, with its
/// trimming and refusals, and Remove is <see cref="KeyRing.Remove"/>. A status is "missing" or
/// <see cref="KeyState"/>'s "present (N chars, source)", and a message names the key and at most its length.
/// No text this class returns ever contains a value.
/// </remarks>
internal sealed class KeyScreenModel
{
    /// <summary>The keys without which a turn cannot run: Element Selection needs TypeSafe, every lesson and explanation DeepSeek.</summary>
    public static readonly IReadOnlyList<string> Required = new[] { "deepseek", "typesafe" };

    private readonly KeyRing _ring;

    public KeyScreenModel(KeyRing ring) => _ring = ring;

    public IReadOnlyList<KeyName> Keys => KeyRing.Known;

    /// <summary>True when a required key is in neither Credential Manager nor the environment: the screen opens by itself at start.</summary>
    public bool NeedsSetup => Required.Any(Missing);

    public bool Missing(string name) => _ring.Describe(name).Source == KeySource.Missing;

    public string Status(string name) => Missing(name) ? "missing" : _ring.Describe(name).ToString();

    /// <summary>What each key is for, in the words the screen shows.</summary>
    public static string Purpose(string name) => name switch
    {
        "deepseek" => "DeepSeek: lessons, explanations, and the Vision check (required)",
        "typesafe" => "TypeSafe: finding things on screen and choosing how to teach (required)",
        "fish" => "Fish Audio: the spoken voice (optional)",
        _ => name,
    };

    /// <summary>Stores what was typed. Returns the message to show; never throws for a bad key or a failing store.</summary>
    public string Save(string name, string typed)
    {
        try
        {
            var length = _ring.Store(name, typed);
            return $"Saved the {name} key in Windows Credential Manager ({length} chars).";
        }
        catch (ArgumentException ex)
        {
            // KeyRing's refusals ("the key is empty", "... contains a space ...") describe the shape, never the value.
            // .NET appends " (Parameter 'value')" to the message; the screen shows the sentence alone.
            return $"Not saved: {ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty, StringComparison.Ordinal)}.";
        }
        catch (SecretStoreException)
        {
            return $"Not saved: Windows Credential Manager could not store the {name} key.";
        }
    }

    /// <summary>Removes the stored key. An environment variable, which the ring still reads, is said, not touched.</summary>
    public string Remove(string name)
    {
        bool removed;
        try
        {
            removed = _ring.Remove(name);
        }
        catch (SecretStoreException)
        {
            return $"Not removed: Windows Credential Manager could not be reached.";
        }

        var variable = KeyRing.Find(name).EnvironmentVariable;
        var stillThere = _ring.Describe(name).Source == KeySource.Environment
            ? $" The {variable} environment variable still provides one."
            : string.Empty;
        return (removed ? $"Removed the stored {name} key." : $"No {name} key was stored in Credential Manager.") + stillThere;
    }
}
