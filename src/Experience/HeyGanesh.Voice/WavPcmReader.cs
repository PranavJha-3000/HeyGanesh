using System.Text;

namespace HeyGanesh.Voice;

/// <summary>
/// Reads a WAV byte stream and yields its data chunk as <see cref="TtsAudioChunk"/>s of raw PCM.
/// </summary>
/// <remarks>
/// <para>
/// The module's only container-aware code, and its strictest validation point: on the first header it
/// requires exactly what the playback side can consume — RIFF/WAVE, format tag 1 (uncompressed PCM),
/// 16 bits, one channel — and fails loudly on anything else rather than playing garbage. Not a general
/// WAV decoder: unsupported shapes are errors, never fallbacks.
/// </para>
/// <para>
/// Public so the verification host can feed it recorded provider bytes and assert the parse offline;
/// the alternative is verifying the codebase's only audio parser exclusively against a live, billable
/// API call. Each yielded chunk owns its own array, so callers may hold memory as long as they like.
/// </para>
/// </remarks>
public static class WavPcmReader
{
    /// <summary>
    /// Yields the WAV's data chunk as PCM chunks of at most <paramref name="maxChunkBytes"/> bytes. The
    /// final chunk carries <see cref="TtsAudioChunk.IsFinal"/>; an empty payload still ends with one, so a
    /// player can tell "done" from "the stream died".
    /// </summary>
    /// <exception cref="VoiceClientException">Not a supported WAV (bad magic, not PCM, wrong bit depth or channels, truncation).</exception>
    public static async IAsyncEnumerable<TtsAudioChunk> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        int maxChunkBytes = 32 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChunkBytes, 1024);

        var prefix = new byte[12];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);

        if (prefix[0] != 'R' || prefix[1] != 'I' || prefix[2] != 'F' || prefix[3] != 'F' ||
            prefix[8] != 'W' || prefix[9] != 'A' || prefix[10] != 'V' || prefix[11] != 'E')
        {
            throw new VoiceClientException(
                "The TTS provider did not return WAV data (missing RIFF/WAVE magic) although format=wav " +
                "was requested. Refusing to play bytes whose shape we cannot trust.");
        }

        var sampleRate = 0;
        long dataRemaining = -1; // -1 = the server did not state a data length (streaming-style header).
        var header = new byte[8];

        // Walk RIFF chunks until data. fmt comes first in practice; any other chunk (LIST, fact, ...)
        // is self-describing and skipped rather than special-cased.
        while (dataRemaining < 0)
        {
            var got = await ReadUpToAsync(stream, header, cancellationToken).ConfigureAwait(false);
            if (got < header.Length)
            {
                throw new VoiceClientException(
                    got == 0
                        ? "The TTS provider returned a WAV with no data chunk."
                        : "The TTS provider's WAV stream ended inside a chunk header.");
            }

            var id = Encoding.ASCII.GetString(header, 0, 4);
            var size = BitConverter.ToUInt32(header, 4);

            if (id == "fmt ")
            {
                var fmt = new byte[16];
                await ReadExactlyAsync(stream, fmt, cancellationToken).ConfigureAwait(false);
                var formatTag = BitConverter.ToUInt16(fmt, 0);
                var channels = BitConverter.ToUInt16(fmt, 2);
                sampleRate = (int)BitConverter.ToUInt32(fmt, 4);
                var bits = BitConverter.ToUInt16(fmt, 14);

                if (formatTag != 1 || channels != 1 || bits != 16 || sampleRate <= 0)
                {
                    // ITtsClient's contract with IAudioOutput is decoded mono PCM16. Anything else would
                    // be played at the wrong rate or shape — an error, not a fallback.
                    throw new VoiceClientException(
                        $"The TTS provider returned an unsupported WAV shape (formatTag={formatTag}, " +
                        $"channels={channels}, bits={bits}, rate={sampleRate}); expected uncompressed " +
                        "PCM, mono, 16-bit.");
                }

                if (size > 16)
                {
                    await SkipAsync(stream, size - 16, cancellationToken).ConfigureAwait(false);
                }

                continue;
            }

            if (id == "data")
            {
                if (sampleRate <= 0)
                {
                    throw new VoiceClientException("The TTS provider's WAV listed data before fmt; cannot play it.");
                }

                dataRemaining = size == uint.MaxValue ? -1 : size;
                break;
            }

            await SkipAsync(stream, size, cancellationToken).ConfigureAwait(false);
        }

        if (dataRemaining == 0)
        {
            yield return new TtsAudioChunk { Pcm = ReadOnlyMemory<byte>.Empty, SampleRate = sampleRate, IsFinal = true };
            yield break;
        }

        var scratch = new byte[maxChunkBytes];
        while (true)
        {
            var want = scratch.Length;
            if (dataRemaining >= 0 && dataRemaining < want)
            {
                want = (int)dataRemaining;
            }

            var got = want == 0
                ? 0
                : await ReadUpToAsync(stream, scratch.AsMemory(0, want), cancellationToken).ConfigureAwait(false);

            if (got == 0)
            {
                if (dataRemaining > 0)
                {
                    throw new VoiceClientException(
                        "The TTS provider's WAV data chunk was truncated: the stream ended before its stated size.");
                }

                // Unstated length + EOF: the previous chunk was content, not completion, so the marker
                // arrives here as its own empty final chunk (WinMmAudioOutput treats empty as "done").
                yield return new TtsAudioChunk { Pcm = ReadOnlyMemory<byte>.Empty, SampleRate = sampleRate, IsFinal = true };
                yield break;
            }

            if (dataRemaining >= 0)
            {
                dataRemaining -= got;
            }

            // Fresh array per chunk: TtsAudioChunk's memory is caller-owned for as long as they need it.
            var owned = new byte[got];
            Buffer.BlockCopy(scratch, 0, owned, 0, got);

            yield return new TtsAudioChunk
            {
                Pcm = owned,
                SampleRate = sampleRate,
                IsFinal = dataRemaining == 0,
            };

            if (dataRemaining == 0)
            {
                yield break;
            }
        }
    }

    /// <summary>Reads exactly 12 (or 16) header bytes; a short read means the provider cut the stream.</summary>
    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (got == 0)
            {
                throw new VoiceClientException("The TTS provider's WAV stream ended unexpectedly.");
            }

            offset += got;
        }
    }

    /// <summary>Reads up to the buffer's length; 0 means end-of-stream.</summary>
    private static Task<int> ReadUpToAsync(Stream stream, Memory<byte> buffer, CancellationToken ct) =>
        ReadUpToCoreAsync(stream, buffer, ct);

    private static async Task<int> ReadUpToCoreAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (got == 0)
            {
                break;
            }

            offset += got;
        }

        return offset;
    }

    private static async Task SkipAsync(Stream stream, long count, CancellationToken ct)
    {
        var scratch = new byte[4096];
        var remaining = count;
        while (remaining > 0)
        {
            var want = (int)Math.Min(scratch.Length, remaining);
            var got = await stream.ReadAsync(scratch.AsMemory(0, want), ct).ConfigureAwait(false);
            if (got == 0)
            {
                throw new VoiceClientException("The TTS provider's WAV stream ended inside a skipped chunk.");
            }

            remaining -= got;
        }
    }
}
