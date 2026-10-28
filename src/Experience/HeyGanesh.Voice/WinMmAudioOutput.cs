namespace HeyGanesh.Voice;

/// <summary>
/// The real speaker: winmm waveform playback of decoded PCM, one chunk at a time.
/// </summary>
/// <remarks>
/// Opened with <c>CALLBACK_EVENT</c> and a plain event handle (winmm sets it when a buffer finishes), so
/// playback is "write, then wait for the device" with no per-chunk callback thunk to keep alive — the
/// pattern that made capture's interop hairier. The device opens lazily on the first chunk at that
/// chunk's rate and closes when the utterance ends. Cancellation is a real stop: the token's registration
/// calls <c>waveOutReset</c>, which drops pending buffers and lets the wait unwind as a clean
/// <see cref="OperationCanceledException"/> — no audio keeps playing after the caller gave up.
/// </remarks>
public sealed class WinMmAudioOutput : IAudioOutput
{
    private const uint WaveMapper = 0xFFFFFFFF;

    // CALLBACK_EVENT for waveform audio is 0x00050000 — NOT 0x00010000, which is CALLBACK_WINDOW and
    // made waveOutOpen reject the event handle as a window with "invalid parameter" (error 11) until
    // this constant was corrected against mmsystem.h.
    private const uint CallbackEvent = 0x00050000;

    private readonly object _gate = new();
    private readonly AutoResetEvent _done = new(false);

    private IntPtr _device = IntPtr.Zero;
    private int _deviceRate;
    private bool _disposed;

    /// <summary>Plays the stream to completion, or aborts it promptly on cancellation.</summary>
    /// <exception cref="VoiceClientException">The output device could not be opened or a write failed.</exception>
    public async Task PlayAsync(IAsyncEnumerable<TtsAudioChunk> audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await foreach (var chunk in audio.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Empty chunks are completion markers from the reader, not silence to play.
            if (chunk.Pcm.IsEmpty)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            PlayChunk(chunk, cancellationToken);
        }
    }

    private void PlayChunk(TtsAudioChunk chunk, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            OpenDevice(chunk.SampleRate);

            var pcm = chunk.Pcm;
            var headerPointer = IntPtr.Zero;
            var bufferPointer = IntPtr.Zero;

            try
            {
                bufferPointer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pcm.Length);
                System.Runtime.InteropServices.Marshal.Copy(pcm.ToArray(), 0, bufferPointer, pcm.Length);

                var headerSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WinMmPInvoke.WaveHdr>();
                headerPointer = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)headerSize);
                System.Runtime.InteropServices.Marshal.StructureToPtr(
                    new WinMmPInvoke.WaveHdr { Data = bufferPointer, BufferLength = (uint)pcm.Length },
                    headerPointer,
                    fDeleteOld: false);

                ThrowIfFailed(
                    WinMmPInvoke.waveOutPrepareHeader(_device, headerPointer, headerSize),
                    "prepare an output buffer");

                // The reset-on-cancel registration runs on the cancelling thread: waveOutReset completes
                // the in-flight buffer, the event fires, and the wait below returns so we can throw.
                using var registration = cancellationToken.Register(
                    static state =>
                    {
                        var (device, done) = ((IntPtr, AutoResetEvent))state!;
                        if (device != IntPtr.Zero)
                        {
                            WinMmPInvoke.waveOutReset(device);
                        }

                        done.Set();
                    },
                    (_device, _done));

                ThrowIfFailed(
                    WinMmPInvoke.waveOutWrite(_device, headerPointer, headerSize),
                    "queue audio for playback");

                _done.WaitOne();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                if (headerPointer != IntPtr.Zero)
                {
                    WinMmPInvoke.waveOutUnprepareHeader(
                        _device, headerPointer, (uint)System.Runtime.InteropServices.Marshal.SizeOf<WinMmPInvoke.WaveHdr>());
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(headerPointer);
                }

                if (bufferPointer != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(bufferPointer);
                }
            }
        }
    }

    /// <summary>Opens the default output device at the first chunk's rate. Caller holds <see cref="_gate"/>.</summary>
    private void OpenDevice(int sampleRate)
    {
        if (_device != IntPtr.Zero)
        {
            if (_deviceRate == sampleRate)
            {
                return;
            }

            // A provider that changed rate mid-stream would otherwise play at the wrong pitch: close and
            // reopen rather than resample — there is no resampler in this module, on purpose.
            CloseDevice();
        }

        var format = WinMmPInvoke.WaveFormatEx.Mono16(sampleRate);
        var opened = WinMmPInvoke.waveOutOpen(
            out var device,
            WaveMapper,
            ref format,
            _done.SafeWaitHandle.DangerousGetHandle(),
            IntPtr.Zero,
            CallbackEvent);

        if (opened != WinMmPInvoke.MmSysErrNoError)
        {
            throw new VoiceClientException($"The speaker could not be opened: {WinMmPInvoke.Describe(opened)}.");
        }

        _device = device;
        _deviceRate = sampleRate;
    }

    private void CloseDevice()
    {
        if (_device != IntPtr.Zero)
        {
            WinMmPInvoke.waveOutReset(_device);
            WinMmPInvoke.waveOutClose(_device);
            _device = IntPtr.Zero;
            _deviceRate = 0;
        }
    }

    private static void ThrowIfFailed(int result, string what)
    {
        if (result != WinMmPInvoke.MmSysErrNoError)
        {
            throw new VoiceClientException($"The speaker could not {what}: {WinMmPInvoke.Describe(result)}.");
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
            CloseDevice();
        }

        _done.Dispose();
    }
}
