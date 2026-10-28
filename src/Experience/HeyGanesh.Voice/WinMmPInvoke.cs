using System.Runtime.InteropServices;

namespace HeyGanesh.Voice;

/// <summary>
/// The winmm surface this module uses: waveform capture (<c>waveIn*</c>) for the microphone and waveform
/// playback (<c>waveOut*</c>) for the speaker.
/// </summary>
/// <remarks>
/// <para>
/// P/Invoke rather than a package, for the reason DEC-001 gives for the hotkey hook: the managed
/// alternative is a NuGet dependency (NAudio and friends) for behaviour that is a few hundred lines of
/// owned interop, and the repo's zero-dependency invariant is worth more than the saved lines.
/// </para>
/// <para>
/// Declared here as <c>internal</c> so nothing outside this assembly can open a device behind the module's
/// back — <see cref="IMicrophoneSource"/> and <see cref="IAudioOutput"/> are the only supported routes.
/// These calls only ever move audio in and out of this process: nothing here touches the input queue, the
/// cursor, the clipboard, or any window (the "no automatic computer actions" rule).
/// </para>
/// </remarks>
internal static class WinMmPInvoke
{
    internal const uint WaveMapper = 0xFFFFFFFF;
    internal const uint CallbackFunction = 0x00030000;
    internal const ushort WaveFormatPcm = 1;
    internal const ushort BitsPerSample16 = 16;
    internal const ushort ChannelsMono = 1;

    /// <summary><c>WIM_DATA</c> — a capture buffer has been filled.</summary>
    internal const uint WimData = 0x03C0;

    /// <summary><c>WHDR_DONE</c> — a buffer finished playing.</summary>
    internal const uint WhdrDone = 0x00000001;

    /// <summary><c>WHDR_PREPARED</c> — a buffer is prepared.</summary>
    internal const uint WhdrPrepared = 0x00000002;

    /// <summary><c>MMSYSERR_NOERROR</c>.</summary>
    internal const int MmSysErrNoError = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveFormatEx
    {
        internal ushort FormatTag;
        internal ushort Channels;
        internal uint SamplesPerSec;
        internal uint AvgBytesPerSec;
        internal ushort BlockAlign;
        internal ushort BitsPerSample;
        internal ushort ExtraSize;

        /// <summary>Mono 16-bit PCM at <paramref name="sampleRate"/> — the only shape this module uses.</summary>
        internal static WaveFormatEx Mono16(int sampleRate) => new()
        {
            FormatTag = WaveFormatPcm,
            Channels = ChannelsMono,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * 2),
            BlockAlign = 2,
            BitsPerSample = BitsPerSample16,
            ExtraSize = 0,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveHdr
    {
        internal IntPtr Data;
        internal uint BufferLength;
        internal uint BytesRecorded;
        internal IntPtr User;
        internal uint Flags;
        internal uint Loops;
        internal IntPtr Next;
        internal IntPtr Reserved;
    }

    /// <summary>The <c>waveInProc</c> / <c>waveOutProc</c> shape (both are <c>void CALLBACK</c>).</summary>
    internal delegate void WaveProc(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll")]
    internal static extern int waveInOpen(out IntPtr waveIn, uint deviceId, [In] ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    internal static extern int waveInPrepareHeader(IntPtr waveIn, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveInUnprepareHeader(IntPtr waveIn, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveInAddBuffer(IntPtr waveIn, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveInStart(IntPtr waveIn);

    [DllImport("winmm.dll")]
    internal static extern int waveInStop(IntPtr waveIn);

    [DllImport("winmm.dll")]
    internal static extern int waveInReset(IntPtr waveIn);

    [DllImport("winmm.dll")]
    internal static extern int waveInClose(IntPtr waveIn);

    [DllImport("winmm.dll")]
    internal static extern int waveOutOpen(out IntPtr waveOut, uint deviceId, [In] ref WaveFormatEx format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    internal static extern int waveOutPrepareHeader(IntPtr waveOut, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveOutUnprepareHeader(IntPtr waveOut, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveOutWrite(IntPtr waveOut, IntPtr header, uint headerSize);

    [DllImport("winmm.dll")]
    internal static extern int waveOutReset(IntPtr waveOut);

    [DllImport("winmm.dll")]
    internal static extern int waveOutClose(IntPtr waveOut);

    /// <summary>
    /// Turns a winmm return code into something a human can act on. The codes are not exception messages,
    /// and "MMSYSERR 4" in a log tells nobody that the machine has no microphone.
    /// </summary>
    internal static string Describe(int code) => code switch
    {
        0 => "no error",
        1 => "a winmm error occurred (MMSYSERR_ERROR)",
        2 => "bad device id (MMSYSERR_BADDEVICEID)",
        3 => "the driver reported a not-enabled problem (MMSYSERR_NOTENABLED)",
        4 => "the device is already in use (MMSYSERR_ALLOCATED) — is another app holding the microphone or speaker?",
        5 => "invalid device handle (MMSYSERR_INVALHANDLE)",
        6 => "no device driver is present (MMSYSERR_NODRIVER) — is there a microphone or speaker attached?",
        7 => "the device does not support this format (MMSYSERR_NOTSUPPORTED)",
        8 => "bad structure size (MMSYSERR_STATSIZE)",
        9 => "bad error number (MMSYSERR_BADERRNUM)",
        10 => "bad flag (MMSYSERR_INVALFLAG) — usually a waveIn/waveOut callback flag mismatch",
        11 => "bad parameter (MMSYSERR_INVALPARAM) — usually a callback handle of the wrong kind",
        12 => "the device handle is busy (MMSYSERR_HANDLEBUSY)",
        13 => "invalid alias (MMSYSERR_INVALIDALIAS)",
        _ => $"winmm error {code}",
    };
}
