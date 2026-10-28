using System.Runtime.InteropServices;

namespace HeyGanesh.Voice;

/// <summary>
/// The real microphone: winmm waveform capture, mono 16-bit PCM, four rotating buffers.
/// </summary>
/// <remarks>
/// <para>
/// Rotation rather than one buffer because winmm fills buffers asynchronously: with a single buffer the
/// device would have nothing to write into while the consumer copied the previous block, and that gap
/// shows up as clipped speech. Four 100 ms buffers give ~400 ms of slack.
/// </para>
/// <para>
/// The callback is a static delegate held in a static field so the GC cannot collect the thunk while
/// winmm still holds its raw function pointer — the classic way this interop crashes. The instance is
/// found through a <see cref="GCHandle"/> passed as winmm's <c>dwInstance</c>, not through thread state,
/// because winmm calls back on its own thread.
/// </para>
/// <para>
/// <see cref="Stop"/> takes the same lock the callback takes, which is what stops a late <c>WIM_DATA</c>
/// from re-queuing a buffer into a device that is being closed. The lock is reentrant, so the synchronous
/// callbacks winmm may raise from inside <c>waveInReset</c> cannot deadlock.
/// </para>
/// </remarks>
public sealed class WinMmMicrophoneSource : IMicrophoneSource
{
    /// <summary>16 kHz: the rate the Gateway's realtime frontends consume for input.</summary>
    public const int DefaultSampleRate = 16000;

    private const int BuffersInFlight = 4;
    private const int BlockMilliseconds = 100;

    // Rooted for the lifetime of the process: winmm keeps only a raw pointer to this thunk.
    private static readonly WinMmPInvoke.WaveProc CallbackProc = OnWaveCallback;

    private readonly int _sampleRate;
    private readonly int _blockBytes;
    private readonly object _gate = new();
    private readonly IntPtr[] _headers = new IntPtr[BuffersInFlight];
    private readonly IntPtr[] _buffers = new IntPtr[BuffersInFlight];
    private readonly GCHandle _self;

    private IntPtr _device = IntPtr.Zero;
    private bool _started;
    private bool _disposed;

    public WinMmMicrophoneSource(int sampleRate = DefaultSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        _sampleRate = sampleRate;
        _blockBytes = sampleRate * 2 * BlockMilliseconds / 1000;
        _self = GCHandle.Alloc(this);
    }

    /// <summary>Samples per second this source produces.</summary>
    public int SampleRate => _sampleRate;

    /// <summary>Raised on the winmm capture thread for each filled buffer.</summary>
    public event EventHandler<MicrophoneFrameEventArgs>? Frame;

    /// <summary>Opens the default capture device and starts filling buffers.</summary>
    /// <exception cref="VoiceClientException">No capture device could be opened.</exception>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            var format = WinMmPInvoke.WaveFormatEx.Mono16(_sampleRate);
            var opened = WinMmPInvoke.waveInOpen(
                out var device,
                WinMmPInvoke.WaveMapper,
                ref format,
                Marshal.GetFunctionPointerForDelegate(CallbackProc),
                GCHandle.ToIntPtr(_self),
                WinMmPInvoke.CallbackFunction);

            if (opened != WinMmPInvoke.MmSysErrNoError)
            {
                throw new VoiceClientException($"The microphone could not be opened: {WinMmPInvoke.Describe(opened)}.");
            }

            _device = device;
            var headerSize = (uint)Marshal.SizeOf<WinMmPInvoke.WaveHdr>();

            try
            {
                for (var i = 0; i < BuffersInFlight; i++)
                {
                    _buffers[i] = Marshal.AllocHGlobal(_blockBytes);
                    _headers[i] = Marshal.AllocHGlobal((int)headerSize);
                    Marshal.StructureToPtr(
                        new WinMmPInvoke.WaveHdr { Data = _buffers[i], BufferLength = (uint)_blockBytes },
                        _headers[i],
                        fDeleteOld: false);

                    ThrowIfFailed(WinMmPInvoke.waveInPrepareHeader(_device, _headers[i], headerSize), "prepare a capture buffer");
                    ThrowIfFailed(WinMmPInvoke.waveInAddBuffer(_device, _headers[i], headerSize), "queue a capture buffer");
                }

                ThrowIfFailed(WinMmPInvoke.waveInStart(_device), "start capture");
                _started = true;
            }
            catch
            {
                // A half-open device would leak a handle and, worse, keep the microphone indicator lit.
                _started = false;
                ReleaseDevice();
                throw;
            }
        }
    }

    /// <summary>Stops capture and releases the device. Safe to call when not started, and idempotent.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_device == IntPtr.Zero)
            {
                return;
            }

            // Cleared before the reset so a callback arriving during teardown does not re-queue buffers
            // into a device that is about to close.
            _started = false;
            WinMmPInvoke.waveInStop(_device);
            WinMmPInvoke.waveInReset(_device);
            ReleaseDevice();
        }
    }

    /// <summary>Unprepares and frees every buffer, then closes the device. Caller must hold <see cref="_gate"/>.</summary>
    private void ReleaseDevice()
    {
        var headerSize = (uint)Marshal.SizeOf<WinMmPInvoke.WaveHdr>();

        for (var i = 0; i < BuffersInFlight; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                WinMmPInvoke.waveInUnprepareHeader(_device, _headers[i], headerSize);
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }

            if (_buffers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffers[i]);
                _buffers[i] = IntPtr.Zero;
            }
        }

        if (_device != IntPtr.Zero)
        {
            WinMmPInvoke.waveInClose(_device);
            _device = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Copies a filled buffer out and re-queues it. This runs on winmm's thread, so it does the minimum:
    /// copy, re-queue, then hand the copy to subscribers <i>outside</i> the lock — a slow subscriber must
    /// never be able to stall the capture device.
    /// </summary>
    private void HandleBuffer(IntPtr headerPointer)
    {
        byte[]? copy = null;

        lock (_gate)
        {
            if (_disposed || _device == IntPtr.Zero)
            {
                return;
            }

            var header = Marshal.PtrToStructure<WinMmPInvoke.WaveHdr>(headerPointer);
            var recorded = (int)Math.Min(header.BytesRecorded, header.BufferLength);
            if (recorded > 0)
            {
                copy = new byte[recorded];
                Marshal.Copy(header.Data, copy, 0, recorded);
            }

            if (_started)
            {
                WinMmPInvoke.waveInAddBuffer(
                    _device, headerPointer, (uint)Marshal.SizeOf<WinMmPInvoke.WaveHdr>());
            }
        }

        if (copy is not null)
        {
            Frame?.Invoke(this, new MicrophoneFrameEventArgs(copy));
        }
    }

    private static void OnWaveCallback(IntPtr handle, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (message != WinMmPInvoke.WimData || instance == IntPtr.Zero)
        {
            return;
        }

        if (GCHandle.FromIntPtr(instance).Target is WinMmMicrophoneSource source)
        {
            source.HandleBuffer(param1);
        }
    }

    private static void ThrowIfFailed(int result, string what)
    {
        if (result != WinMmPInvoke.MmSysErrNoError)
        {
            throw new VoiceClientException($"The microphone could not {what}: {WinMmPInvoke.Describe(result)}.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}
