using System.Diagnostics;
using HeyGanesh.Abstractions;
using Windows.Graphics.Capture;

namespace HeyGanesh.ScreenCapture;

/// <summary>Tuning for <see cref="ScreenCaptureService"/>.</summary>
public sealed class ScreenCaptureOptions
{
    /// <summary>Defaults: 1280 px cap, JPEG quality 0.8, cursor excluded, two settle frames.</summary>
    public static ScreenCaptureOptions Default { get; } = new();

    /// <summary>
    /// Longest edge of the encoded image, in pixels, or the original size when already smaller.
    /// Never upscales. 1280 keeps a full desktop legible to a vision model while staying well
    /// inside typical per-image upload limits.
    /// </summary>
    public int MaxDimension { get; init; } = 1280;

    /// <summary>JPEG quality factor, 0..1. 0.8 mirrors Clicky's setting.</summary>
    public float JpegQuality { get; init; } = 0.8f;

    /// <summary>
    /// Whether the mouse cursor is drawn into the frame. Defaults to <see langword="false"/>: the
    /// cursor is not part of the screen content a vision model should interpret, and it can be
    /// caught mid-motion over the very element being asked about.
    /// </summary>
    public bool IncludeCursor { get; init; }

    /// <summary>
    /// How many frames to observe before using one. The first frame after starting a capture can be
    /// stale or black on some drivers, so the default of 2 prefers a later frame when one arrives.
    /// </summary>
    /// <remarks>
    /// This is a <i>preference</i>, not a requirement — see <see cref="SettleTimeout"/>. WGC
    /// delivers frames in response to screen changes, so on a still desktop there may never be a
    /// second frame at all.
    /// </remarks>
    public int SettleFrames { get; init; } = 2;

    /// <summary>
    /// How long to wait for the preferred later frame once a first frame is already in hand.
    /// </summary>
    /// <remarks>
    /// Deliberately short. Measured behaviour is that WGC delivers exactly one frame for a static
    /// desktop, so waiting longer buys nothing but delay: the first frame is used as soon as this
    /// window closes. If a second frame does arrive inside the window, it is preferred.
    /// </remarks>
    public TimeSpan SettleTimeout { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long to wait for <i>any</i> frame before failing the capture. This is the hard deadline;
    /// it is not the settle window.
    /// </summary>
    public TimeSpan FrameTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether to measure how much of the frame is non-black. On by default: one extra pass over
    /// pixels already in memory is negligible next to what happens downstream, and a blank capture
    /// should never be handed to a vision model.
    /// </summary>
    public bool ComputeNonBlackRatio { get; init; } = true;

    /// <summary>
    /// Whether to read the frame's text with Windows' built-in OCR before it is downscaled (DEC-038).
    /// Off by default. When on, <see cref="ScreenCaptureService.CaptureAllMonitorsAsync"/> reads only
    /// the first capture — the cursor's monitor, where the user is looking — and
    /// <see cref="ScreenCaptureService.CaptureMonitorAsync"/> reads the monitor it was asked for.
    /// About 135 ms on a 2560×1600 screen. A missing OCR language or an OCR failure never fails the
    /// capture: the frame's <c>TextNote</c> says why there is no text.
    /// </summary>
    public bool ReadText { get; init; }
}

/// <summary>One monitor's still frame, ready to upload.</summary>
/// <param name="Monitor">Which display this came from.</param>
/// <param name="SourceWidth">Captured width in physical pixels, before scaling.</param>
/// <param name="SourceHeight">Captured height in physical pixels, before scaling.</param>
/// <param name="OutputWidth">Width of the encoded JPEG.</param>
/// <param name="OutputHeight">Height of the encoded JPEG.</param>
/// <param name="Jpeg">The encoded image bytes.</param>
/// <param name="NonBlackPixelRatio">
/// Fraction of source pixels that are not black, 0..1, or <see cref="double.NaN"/> when
/// <see cref="ScreenCaptureOptions.ComputeNonBlackRatio"/> is disabled. Near zero means the frame
/// has no content and should be retried rather than interpreted.
/// </param>
/// <param name="FramesObserved">How many frames the capture session delivered before one was used.</param>
/// <param name="Duration">Wall-clock time for this monitor's capture and encode.</param>


/// <summary>
/// Screen Capture — on-demand still frames of every connected monitor, via
/// <c>Windows.Graphics.Capture</c>.
/// </summary>
/// <remarks>
/// <para><b>On demand only.</b> Nothing is captured until <see cref="CaptureMonitorAsync"/> or
/// <see cref="CaptureAllMonitorsAsync"/> is called. Each call opens a capture session, takes one
/// frame, and closes it again — a future hotkey press maps to exactly one call, not to a
/// continuously running capture.</para>
///
/// <para><b>Ordering.</b> <see cref="CaptureAllMonitorsAsync"/> returns the monitor holding the
/// mouse cursor first — that is where the user is looking, and therefore the highest-value image —
/// then the primary display, then the rest in enumeration order. Cursor position is sampled once
/// when the call starts, so ordering is a consistent snapshot even if the pointer moves during the
/// capture.</para>
///
/// <para><b>Sequential by design.</b> Monitors are captured one after another, not in parallel, so
/// the output order is exactly the documented priority order and only one capture border is ever
/// on screen at a time.</para>
///
/// <para><b>Coordinates are physical pixels.</b> Frames arrive at the monitor's true resolution, so
/// monitor bounds and the cursor test share that unvirtualised space. A host that is not
/// per-monitor-DPI-aware will see <c>GetCursorPos</c> report virtualised coordinates that disagree
/// with these bounds on scaled displays. This module deliberately does <b>not</b> change process
/// DPI awareness — that is process-global and belongs to the app shell — so a host should declare
/// <c>PerMonitorV2</c> itself; the verification host demonstrates doing so.</para>
///
/// <para><b>HeyGanesh's own windows</b> are not filtered out of these frames. They are marked at
/// the source by <see cref="CaptureExclusion"/>, so the OS omits them from capture output
/// entirely. See that type for the contract the Screen Overlay must honour.</para>
///
/// <para><b>Threading.</b> Fully async with no thread affinity and no message pump, so it is safe
/// to call from a UI thread — the work happens on the thread pool and capture callbacks land on the
/// frame pool's own worker thread.</para>
/// </remarks>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly ScreenCaptureOptions _options;
    private readonly WgcFrameGrabber _grabber;

    // Created only when ReadText is on, so a capture that never reads text never loads the recogniser.
    private readonly ScreenTextReader? _textReader;
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private bool _disposed;

    public ScreenCaptureService(ScreenCaptureOptions? options = null)
    {
        _options = options ?? ScreenCaptureOptions.Default;
        Validate(_options);
        _grabber = new WgcFrameGrabber(_options);
        _textReader = _options.ReadText ? new ScreenTextReader() : null;
    }

    /// <summary>The options this service was created with.</summary>
    public ScreenCaptureOptions Options => _options;

    /// <summary>Whether the OS reports that screen capture is available on this device.</summary>
    public static bool IsCaptureSupported() => GraphicsCaptureSession.IsSupported();

    /// <summary>Enumerates connected displays in the order Windows reports them.</summary>
    public IReadOnlyList<MonitorInfo> EnumerateMonitors() => Monitors.Enumerate();

    /// <summary>
    /// The display currently holding the mouse cursor — the equivalent of Clicky's "primary focus
    /// screen" — or <see langword="null"/> if it cannot be resolved.
    /// </summary>
    public MonitorInfo? GetCursorMonitor()
    {
        var monitors = Monitors.Enumerate();
        return Monitors.FindCursorMonitor(monitors);
    }

    /// <summary>Captures a single monitor as one JPEG.</summary>
    public async Task<MonitorCapture> CaptureMonitorAsync(
        MonitorInfo monitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await CaptureCoreAsync(monitor, readText: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    /// <summary>
    /// Captures every connected monitor, the cursor's display first, as one JPEG each.
    /// </summary>
    public async Task<IReadOnlyList<MonitorCapture>> CaptureAllMonitorsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var monitors = Monitors.Enumerate();

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("No connected displays were reported.");
        }

        // Cursor sampled once, here, so the priority order is a snapshot rather than something that
        // can shift halfway through a sequential capture.
        var ordered = Monitors.OrderForCapture(monitors, Monitors.FindCursorMonitor(monitors));
        var captures = new List<MonitorCapture>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            // Text is read on the first (cursor) monitor only: the others are context the user is not
            // looking at, and each read costs about 135 ms.
            await _captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                captures.Add(await CaptureCoreAsync(ordered[i], readText: i == 0, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                _captureGate.Release();
            }
        }

        return captures;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _grabber.Dispose();
        _captureGate.Dispose();
    }

    private async Task<MonitorCapture> CaptureCoreAsync(MonitorInfo monitor, bool readText, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        var (bitmap, framesObserved) = await _grabber.GrabAsync(monitor, cancellationToken).ConfigureAwait(false);

        using (bitmap)
        {
            var nonBlackRatio = _options.ComputeNonBlackRatio
                ? CpuFrameReader.ComputeNonBlackRatio(bitmap)
                : double.NaN;

            // Before encoding, on the full-size frame: the downscaled JPEG loses about 40% of the lines.
            ScreenTextRead? text = readText && _textReader is not null
                ? await _textReader.ReadAsync(bitmap, monitor.Bounds, cancellationToken).ConfigureAwait(false)
                : null;

            var (jpeg, outputWidth, outputHeight) = await JpegEncoder
                .EncodeAsync(bitmap, _options.MaxDimension, _options.JpegQuality, cancellationToken)
                .ConfigureAwait(false);

            return new MonitorCapture(
                monitor,
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                outputWidth,
                outputHeight,
                jpeg,
                nonBlackRatio,
                framesObserved,
                Stopwatch.GetElapsedTime(started))
            {
                TextLines = text?.Lines ?? Array.Empty<ScreenTextLine>(),
                TextNote = text?.Note,
                TextDuration = text?.Duration ?? TimeSpan.Zero,
            };
        }
    }

    private static void Validate(ScreenCaptureOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDimension, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.JpegQuality, 0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.JpegQuality, 1f);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SettleFrames, 1);

        if (options.FrameTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FrameTimeout must be positive.");
        }

        if (options.SettleTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SettleTimeout cannot be negative.");
        }
    }
}

