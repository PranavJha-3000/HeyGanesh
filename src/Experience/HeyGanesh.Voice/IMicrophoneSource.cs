namespace HeyGanesh.Voice;

/// <summary>One block of captured microphone audio, already in the format the Gateway expects.</summary>
public sealed class MicrophoneFrameEventArgs : EventArgs
{
    public MicrophoneFrameEventArgs(ReadOnlyMemory<byte> pcm) => Pcm = pcm;

    /// <summary>PCM16 little-endian mono samples. The block size is the capture device's, not the caller's business.</summary>
    public ReadOnlyMemory<byte> Pcm { get; }
}

/// <summary>
/// The microphone seam: start capture, get frames, stop capture.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a direct P/Invoke inside the Gateway client for two reasons. Verification needs to
/// drive a listen with no capture device present (a fake source), and a future local speech-to-text path
/// would take its audio from somewhere else entirely — a file, a loopback device, a test fixture.
/// </para>
/// <para>
/// Frames are raised on the capture thread. Subscribers must hand the bytes off and return fast: this is
/// the audio thread, and blocking it drops samples.
/// </para>
/// </remarks>
public interface IMicrophoneSource : IDisposable
{
    /// <summary>Sample rate this source produces (mono, 16-bit).</summary>
    int SampleRate { get; }

    /// <summary>Raised for each captured block while started.</summary>
    event EventHandler<MicrophoneFrameEventArgs>? Frame;

    /// <summary>Opens the device and begins raising frames. Throws <see cref="VoiceClientException"/> when no device can be opened.</summary>
    void Start();

    /// <summary>Stops capture and releases the device. Safe to call when not started.</summary>
    void Stop();
}
