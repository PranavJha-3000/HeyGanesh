using System.Runtime.InteropServices;
using System.Text;

namespace HeyGanesh.Security;

/// <summary>
/// <see cref="ISecretStore"/> implementation backed by Windows Credential Manager.
/// Credential target names are derived deterministically from (providerId, keyAlias).
/// Secrets are held in CredentialBlob as raw bytes; no string copies are retained.
/// Native API failures are surfaced as typed <see cref="SecretStoreException"/>, never
/// exposing secret material in messages.
/// </summary>
public sealed class CredentialManagerSecretStore : ISecretStore
{
    private const string TargetPrefix = "HeyGanesh";
    private bool _disposed;

    public SecretHandle? Get(string providerId, string keyAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentException("providerId is required", nameof(providerId));
        if (string.IsNullOrEmpty(keyAlias)) throw new ArgumentException("keyAlias is required", nameof(keyAlias));

        var targetName = MakeTargetName(providerId, keyAlias);
        if (!NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out var credPtr))
        {
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == NativeMethods.ERROR_NOT_FOUND) return null;
            throw new SecretStoreException($"Credential read failed for provider '{providerId}' alias '{keyAlias}'", lastError);
        }

        try
        {
            var cred = Marshal.PtrToStructure<NativeMethods.Credential>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                return null;

            var blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, (int)cred.CredentialBlobSize);
            return SecretHandle.CreateFromSpan(blob);
        }
        finally
        {
            NativeMethods.CredFree(credPtr);
        }
    }

    public void Set(string providerId, string keyAlias, ReadOnlyMemory<byte> secret)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentException("providerId is required", nameof(providerId));
        if (string.IsNullOrEmpty(keyAlias)) throw new ArgumentException("keyAlias is required", nameof(keyAlias));
        if (secret.IsEmpty) throw new ArgumentException("secret must not be empty", nameof(secret));

        var targetName = MakeTargetName(providerId, keyAlias);

        byte[] secretBytes = secret.ToArray();
        IntPtr blobPtr = IntPtr.Zero;
        try
        {
            blobPtr = Marshal.AllocHGlobal(secretBytes.Length);
            Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);

            var cred = new NativeMethods.Credential
            {
                Flags = NativeMethods.CRED_PRESERVE_CREDENTIAL_BLOB,
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = MarshalStringToCoTaskMemUni(targetName),
                Comment = IntPtr.Zero,
                LastWritten = default,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobPtr,
                Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = IntPtr.Zero,
            };

            if (!NativeMethods.CredWrite(ref cred, 0))
            {
                var lastError = Marshal.GetLastWin32Error();
                throw new SecretStoreException($"Credential write failed for provider '{providerId}' alias '{keyAlias}'", lastError);
            }
        }
        finally
        {
            if (blobPtr != IntPtr.Zero) Marshal.FreeHGlobal(blobPtr);
            Array.Clear(secretBytes, 0, secretBytes.Length);
        }
    }

    public bool Exists(string providerId, string keyAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentException("providerId is required", nameof(providerId));
        if (string.IsNullOrEmpty(keyAlias)) throw new ArgumentException("keyAlias is required", nameof(keyAlias));

        var targetName = MakeTargetName(providerId, keyAlias);
        if (!NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out var credPtr))
        {
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == NativeMethods.ERROR_NOT_FOUND) return false;
            throw new SecretStoreException($"Credential existence check failed for provider '{providerId}' alias '{keyAlias}'", lastError);
        }

        NativeMethods.CredFree(credPtr);
        return true;
    }

    public void Delete(string providerId, string keyAlias)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentException("providerId is required", nameof(providerId));
        if (string.IsNullOrEmpty(keyAlias)) throw new ArgumentException("keyAlias is required", nameof(keyAlias));

        var targetName = MakeTargetName(providerId, keyAlias);
        if (!NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0))
        {
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == NativeMethods.ERROR_NOT_FOUND) return;
            throw new SecretStoreException($"Credential delete failed for provider '{providerId}' alias '{keyAlias}'", lastError);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string MakeTargetName(string providerId, string keyAlias)
        => $"{TargetPrefix}:{providerId}:{keyAlias}";

    private static IntPtr MarshalStringToCoTaskMemUni(string s)
    {
        var bytes = Encoding.Unicode.GetBytes(s + '\0');
        var ptr = Marshal.AllocCoTaskMem(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }
}

/// <summary>
/// Typed error from the secret store. Messages never contain secret material — only
/// providerId/keyAlias identifiers and Win32 error codes.
/// </summary>
public sealed class SecretStoreException : Exception
{
    public int Win32ErrorCode { get; }

    public SecretStoreException(string message, int win32ErrorCode)
        : base(message)
    {
        Win32ErrorCode = win32ErrorCode;
    }
}