using System.Text;

namespace HeyGanesh.Security;

/// <summary>
/// Redacting logger. Wraps Console.WriteLine and suppresses any value that has been
/// registered as a secret (by handle or by raw bytes) or that carries a
/// <see cref="SecretTagAttribute"/>. Non-sensitive diagnostic information is preserved.
/// </summary>
public sealed class SecretLogger : IDisposable
{
    private readonly List<byte[]> _registeredSecrets = new();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        lock (_registeredSecrets)
        {
            foreach (var secret in _registeredSecrets)
                Array.Clear(secret, 0, secret.Length);
            _registeredSecrets.Clear();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>Registers a <see cref="SecretHandle"/> for redaction. The handle's bytes
    /// are copied internally; disposing the handle does not unregister the copy.</summary>
    public void RegisterSecret(SecretHandle handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (handle == null) throw new ArgumentNullException(nameof(handle));

        lock (_registeredSecrets)
        {
            _registeredSecrets.Add(handle.AsMemory().ToArray());
        }
    }

    /// <summary>Registers raw secret bytes for redaction. A copy is made and zeroed on disposal.</summary>
    public void RegisterSecret(ReadOnlyMemory<byte> secret)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (secret.IsEmpty) return;

        lock (_registeredSecrets)
        {
            _registeredSecrets.Add(secret.ToArray());
        }
    }

    /// <summary>Writes a diagnostic line. Any registered secret byte sequence found in
    /// <paramref name="message"/> or in any <paramref name="args"/> is replaced with
    /// <c>[REDACTED]</c>. SecretTag-annotated objects are redacted by type name only.</summary>
    public void Write(string message, params object?[] args)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var redacted = Redact(message);
        if (args != null && args.Length > 0)
        {
            var argStrings = new string[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                argStrings[i] = RedactArg(args[i]);
            }
            redacted = string.Format(redacted, argStrings);
        }

        Console.WriteLine(redacted);
    }

    /// <summary>Writes a diagnostic line with no argument redaction (message only).</summary>
    public void WriteRaw(string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Console.WriteLine(Redact(message));
    }

    private string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        lock (_registeredSecrets)
        {
            foreach (var secret in _registeredSecrets)
            {
                if (secret.Length == 0) continue;
                // Redact the hex representation of the secret (a stable, non-reversible
                // marker) and any direct ASCII/UTF-8 substring match.
                var hex = Convert.ToHexString(secret);
                text = text.Replace(hex, "[REDACTED]");

                var utf8 = Encoding.UTF8.GetString(secret);
                if (!string.IsNullOrEmpty(utf8))
                    text = text.Replace(utf8, "[REDACTED]");
            }
        }
        return text;
    }

    private string RedactArg(object? arg)
    {
        if (arg == null) return "null";

        // SecretHandle is always redacted by type name only.
        if (arg is SecretHandle) return "[SecretHandle: value suppressed]";

        // Any type carrying [SecretTag] on itself is redacted by type name.
        var type = arg.GetType();
        if (type.IsDefined(typeof(SecretTagAttribute), inherit: false))
            return $"[{type.Name}: value suppressed]";

        var s = arg.ToString() ?? string.Empty;
        return Redact(s);
    }

    /// <summary>Convenience: registers a secret then writes a message guaranteed not to
    /// leak it. Equivalent to <see cref="RegisterSecret(SecretHandle)"/> followed by
    /// <see cref="Write(string, object?[])"/>.</summary>
    public void WriteWithSecret(SecretHandle secret, string message, params object?[] args)
    {
        RegisterSecret(secret);
        Write(message, args);
    }
}