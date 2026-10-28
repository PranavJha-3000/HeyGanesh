namespace HeyGanesh.Security;

/// <summary>
/// Opaque handle to secret material held in process memory as <see cref="byte[]"/>.
/// The buffer is zeroed deterministically on disposal so secrets do not linger on the heap.
/// Never convert to string; pass <see cref="AsMemory"/> to the HTTP layer only.
/// </summary>
public sealed class SecretHandle : IDisposable
{
    private byte[]? _buffer;
    private bool _disposed;

    internal SecretHandle(byte[] buffer)
    {
        _buffer = buffer;
    }

    /// <summary>Returns a span over the secret bytes. The caller must not retain the span beyond
    /// the lifetime of this handle.</summary>
    public Span<byte> AsMemory() => _buffer.AsSpan();

    /// <summary>Returns the length of the secret in bytes.</summary>
    public int Length => _buffer?.Length ?? 0;

    /// <summary>True when the handle has been disposed.</summary>
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        if (_buffer != null)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _buffer = null;
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~SecretHandle()
    {
        Dispose();
    }

    public static SecretHandle Create(ReadOnlyMemory<byte> secret)
    {
        var buffer = secret.ToArray();
        return new SecretHandle(buffer);
    }

    internal static SecretHandle CreateFromSpan(ReadOnlySpan<byte> secret)
    {
        var buffer = secret.ToArray();
        return new SecretHandle(buffer);
    }

    public override string ToString() => "[SecretHandle: value suppressed]";
}