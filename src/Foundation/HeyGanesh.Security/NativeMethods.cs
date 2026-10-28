using System.Runtime.InteropServices;

namespace HeyGanesh.Security;

/// <summary>
/// Win32 advapi32 P/Invoke for Windows Credential Manager.
/// Only CredWrite/CredRead/CredDelete are declared. No other advapi32 surface is used.
/// </summary>
internal static class NativeMethods
{
    // Credential types
    internal const uint CRED_TYPE_GENERIC = 1;

    // Persistence: local machine only (enterprise would roam, which we do not want)
    internal const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    internal const uint CRED_PERSIST_ENTERPRISE = 3;

    // Credential flags
    internal const uint CRED_PRESERVE_CREDENTIAL_BLOB = 0x1;

    // Return codes
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_NOT_FOUND = 1168;
    internal const int ERROR_INVALID_PARAMETER = 87;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct Credential
    {
        internal uint Flags;
        internal uint Type;
        internal IntPtr TargetName;
        internal IntPtr Comment;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        internal uint CredentialBlobSize;
        internal IntPtr CredentialBlob;
        internal uint Persist;
        internal uint AttributeCount;
        internal IntPtr Attributes;
        internal IntPtr TargetAlias;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CredWrite(
        [In] ref Credential credential,
        uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CredRead(
        string targetName,
        uint type,
        uint reserved,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CredDelete(
        string targetName,
        uint type,
        uint reserved);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern void CredFree(IntPtr credentialPtr);
}