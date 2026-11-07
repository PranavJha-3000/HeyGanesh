using System.Threading.Channels;

namespace HeyGanesh.DrawStage;

/// <summary>How the voice went: lines spoken; cut short (the speaker failed, so the rest was drawn at once); closed by
/// the person.</summary>
public sealed record DrawStageVoiceResult(int Spoken, bool CutShort, bool Closed);

/// <summary>
/// The speech clock (FEATURE-011 step 4, DEC-071): speaks a drawing's lines in order and tells the page when each
/// starts and ends, so with pace speech each beat's strokes are drawn while its line is heard. openclicky does this
/// inside its answer flow (DrawingAnswer.cs, MIT); here it is on its own, so the speaker is any
/// <c>Func&lt;string, CancellationToken, Task&gt;</c> (the shell's TTS later, a fake or SAPI in the smoke host).
/// <para>
/// Start it before <see cref="DrawStageController.BeginAsync"/> so no line is missed. It ends when every line the
/// program says has been spoken (the page's programRan), when the person closes the drawing, or on
/// <paramref name="cancellationToken"/> (which throws: the caller decides what to do with the stage). If the speaker
/// fails, the rest is drawn at once (finishNow) and nothing more is spoken.
/// </para>
/// </summary>
public static class DrawStageVoice
{
    public static async Task<DrawStageVoiceResult> RunAsync(DrawStageController stage, Func<string, CancellationToken, Task> speak, CancellationToken cancellationToken)
    {
        // Lines arrive on the window's thread, in order, and always before programRan (one ordered message channel).
        var lines = Channel.CreateUnbounded<(int K, string Text)>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var closed = false;
        void Added(int k, string text) => lines.Writer.TryWrite((k, text));
        void Ran(int count) => lines.Writer.TryComplete();
        void Close()
        {
            Volatile.Write(ref closed, true);
            lines.Writer.TryComplete();
            stop.Cancel();
        }

        stage.LineAdded += Added;
        stage.ProgramRan += Ran;
        stage.Closed += Close;
        var spoken = 0;
        try
        {
            await foreach (var (k, text) in lines.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                await stage.LineStartedAsync(k).ConfigureAwait(false);
                try
                {
                    await speak(text, stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await stage.FinishNowAsync().ConfigureAwait(false);
                    return new DrawStageVoiceResult(spoken, CutShort: true, Closed: false);
                }

                await stage.LineEndedAsync(k).ConfigureAwait(false);
                spoken++;
            }

            return new DrawStageVoiceResult(spoken, CutShort: false, Closed: Volatile.Read(ref closed));
        }
        catch (OperationCanceledException) when (Volatile.Read(ref closed) && !cancellationToken.IsCancellationRequested)
        {
            return new DrawStageVoiceResult(spoken, CutShort: false, Closed: true);
        }
        finally
        {
            stage.LineAdded -= Added;
            stage.ProgramRan -= Ran;
            stage.Closed -= Close;
        }
    }
}
