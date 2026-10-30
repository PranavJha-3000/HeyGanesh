using System.Runtime.InteropServices;
using System.Text;

namespace HeyGanesh.Notes;

/// <summary>
/// The few SQLite C functions the store needs, from Windows' own <c>winsqlite3.dll</c> (DEC-058). Strings cross as
/// UTF-8 byte arrays, terminated where SQLite expects it, so no marshalling setting can change their encoding.
/// Nothing else in the module touches native code (NS-1).
/// </summary>
internal static class SqliteNative
{
    private const string Library = "winsqlite3";

    internal const int Ok = 0;
    internal const int Busy = 5;
    internal const int Corrupt = 11;
    internal const int NotADatabase = 26;
    internal const int Row = 100;
    internal const int Done = 101;

    internal const int OpenReadWrite = 0x00000002;
    internal const int OpenCreate = 0x00000004;
    internal const int OpenFullMutex = 0x00010000;

    internal const int TypeNull = 5;

    /// <summary>SQLITE_TRANSIENT: SQLite copies a bound value before the call returns.</summary>
    internal static readonly IntPtr Transient = new(-1);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr sqlite3_libversion();

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_close_v2(IntPtr db);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr sqlite3_errmsg(IntPtr db);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int bytes, IntPtr destructor);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_bind_null(IntPtr stmt, int index);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_column_type(IntPtr stmt, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern long sqlite3_column_int64(IntPtr stmt, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_column_bytes(IntPtr stmt, int column);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern int sqlite3_changes(IntPtr db);

    [DllImport(Library, CallingConvention = CallingConvention.Winapi)]
    internal static extern long sqlite3_last_insert_rowid(IntPtr db);

    /// <summary>A UTF-8 copy with a terminating zero, for the arguments SQLite reads up to the first zero.</summary>
    internal static byte[] Utf8Z(string text)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    /// <summary>Reads a zero-terminated UTF-8 string SQLite owns.</summary>
    internal static string? FromUtf8Z(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);
}
