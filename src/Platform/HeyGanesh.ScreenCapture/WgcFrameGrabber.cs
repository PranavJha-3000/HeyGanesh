using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace HeyGanesh.ScreenCapture;

/// <summary>
/// Acquires one still frame per call from a monitor via Windows.Graphics.Capture.
/// </summary>
/// <remarks>
/// <para><b>One-shot, not streaming.</b> Each call creates a frame pool and a capture session,
/// waits for frames, copies one to CPU memory, then closes everything. Nothing persists between
/// captures, so no capture is running while the user is not asking a question — and only a brief
/// yellow capture border is ever shown.</para>
///
/// <para><b>Free-threaded pool.</b> <c>CreateFreeThreaded</c> is used deliberately: it delivers
/// <c>FrameArrived</c> on the pool's own worker thread and, unlike <c>Create</c>, requires no
/// <c>DispatcherQueue</c> on the installing thread. That is what lets this module run in a plain
/// console host with no UI thread and no message pump.</para>
///
/// <para><b>Settle frames (why the first frame is usually discarded).</b> The first frame after
/// <c>StartCapture</c> can be stale or entirely black on some drivers. With
/// <see cref="ScreenCaptureOptions.SettleFrames"/> at its default of 2, the first frame is dropped
/// and a later frame is used; if that later frame never arrives within
/// <see cref="ScreenCaptureOptions.FrameTimeout"/>, the first frame is accepted rather than failing
/// the capture. <see cref="CpuFrameReader.ComputeNonBlackRatio"/> catches the residual case where
/// even the accepted frame has no content.</para>
///
/// <para><b>Known limits.</b> Nothing is captured while the UAC Secure Desktop is showing. Over a
/// Remote Desktop session the monitor capture item cannot be created at all. On displays with
/// Windows HD Color (HDR) enabled the B8G8R8A8 request tone-maps content, so colours can look
/// washed out — acceptable for vision input, and the price of not carrying a 16-bit pipeline.</para>
///
/// <para><b>Deliberately not done here:</b> <c>GraphicsCaptureSession.IsBorderRequired</c> stays at
/// its default of <see langword="true"/>. Clearing it requires a packaged app that declares the
/// <c>graphicsCaptureWithoutBorder</c> capability plus a user-consented
/// <c>GraphicsCaptureAccess.RequestAccessAsync(Borderless)</c> call; without that consent the setter
/// silently does nothing. That is a decision for the packaged app shell, not this module.</para>
/// </remarks>
internal sealed class WgcFrameGrabber : IDisposable
{
    private const string GraphicsCaptureSessionType = "Windows.Graphics.Capture.GraphicsCaptureSession";
    private const string CursorCapturePropertyName = "IsCursorCaptureEnabled";

    /// <summary>IGraphicsCaptureItem — the interface the interop call is asked to return.</summary>
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    private readonly ScreenCaptureOptions _options;
    private readonly Lazy<IDirect3DDevice> _device;

    internal WgcFrameGrabber(ScreenCaptureOptions options)
    {
        _options = options;

        // One device per service, created on first use. Lazy keeps construction cheap and keeps a
        // D3D device out of hosts that never actually capture anything.
        _device = new Lazy<IDirect3DDevice>(CreateDirect3DDevice, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Captures one frame of <paramref name="monitor"/> and returns it as CPU-readable pixels.
    /// </summary>
    internal async Task<(SoftwareBitmap Bitmap, int FramesObserved)> GrabAsync(
        MonitorInfo monitor,
        CancellationToken cancellationToken)
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException(
                "Windows.Graphics.Capture reports that screen capture is not supported on this device.");
        }

        var itemPointer = CreateItemPointerForMonitor(monitor.Handle);

        try
        {
            // FromAbi does not take ownership, so the pointer returned by CreateForMonitor is held
            // here and released once the capture is finished with the item.
            var item = GraphicsCaptureItem.FromAbi(itemPointer);
            return await GrabItemAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = Marshal.Release(itemPointer);
        }
    }

    public void Dispose()
    {
        // The projected device wrapper and the IInspectable reference behind it are held for the
        // lifetime of this grabber and reclaimed at process exit, rather than being released here.
        // The verification host asserts a clean exit and a flat handle count across captures.
    }

    private async Task<(SoftwareBitmap Bitmap, int FramesObserved)> GrabItemAsync(
        GraphicsCaptureItem item,
        CancellationToken cancellationToken)
    {
        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device.Value,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);

        try
        {
            using var session = pool.CreateCaptureSession(item);
            ApplyCursorPreference(session);

            var (frame, framesObserved) = await AcquireFrameAsync(pool, session, cancellationToken).ConfigureAwait(false);

            if (frame is null)
            {
                throw new TimeoutException(
                    $"No capture frame arrived within {_options.FrameTimeout.TotalMilliseconds:F0} ms.");
            }

            try
            {
                var bitmap = await CpuFrameReader.ReadAsync(frame.Surface, cancellationToken).ConfigureAwait(false);
                return (bitmap, framesObserved);
            }
            finally
            {
                frame.Dispose();
            }
        }
        finally
        {
            pool.Dispose();
        }
    }

    /// <summary>Applies the cursor preference, skipping the property on OS builds that lack it.</summary>
    private void ApplyCursorPreference(GraphicsCaptureSession session)
    {
        if (!ApiInformation.IsPropertyPresent(GraphicsCaptureSessionType, CursorCapturePropertyName))
        {
            return;
        }

        session.IsCursorCaptureEnabled = _options.IncludeCursor;
    }

    /// <summary>
    /// Waits for the settle frames and returns the frame to use, plus how many were observed.
    /// </summary>
    private async Task<(Direct3D11CaptureFrame? Frame, int FramesObserved)> AcquireFrameAsync(
        Direct3D11CaptureFramePool pool,
        GraphicsCaptureSession session,
        CancellationToken cancellationToken)
    {
        var settleFrames = Math.Max(1, _options.SettleFrames);
        var gate = new object();
        var firstFrameArrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settledFrameArrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Direct3D11CaptureFrame? chosen = null;
        var observed = 0;

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var frame = sender.TryGetNextFrame();
            if (frame is null)
            {
                return;
            }

            lock (gate)
            {
                if (observed == 0)
                {
                    observed = 1;
                    chosen = frame;
                    _ = firstFrameArrived.TrySetResult(true);
                    return;
                }

                if (observed < settleFrames)
                {
                    // A later frame is preferred over the one already held; dropping the old frame
                    // also keeps the single-buffer pool from stalling.
                    observed++;
                    chosen?.Dispose();
                    chosen = frame;
                    _ = settledFrameArrived.TrySetResult(true);
                    return;
                }

                // More frames than we asked to observe — release immediately rather than hoarding.
                frame.Dispose();
            }
        }

        pool.FrameArrived += OnFrameArrived;

        try
        {
            session.StartCapture();

            // Phase 1: hard deadline for any frame at all.
            try
            {
                await firstFrameArrived.Task
                    .WaitAsync(_options.FrameTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return (null, 0);
            }

            // Phase 2: short, best-effort window for a preferred later frame. WGC reports frames on
            // screen change, so a still desktop has no second frame to offer — waiting the full
            // frame timeout here was measured at ~2.5 s per capture for no benefit whatsoever.
            if (settleFrames > 1 && _options.SettleTimeout > TimeSpan.Zero)
            {
                try
                {
                    await settledFrameArrived.Task
                        .WaitAsync(_options.SettleTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // Static screen: the frame already in hand is the best available.
                }
            }
        }
        finally
        {
            pool.FrameArrived -= OnFrameArrived;
        }

        lock (gate)
        {
            var result = chosen;
            chosen = null;
            return (result, observed);
        }
    }

    private static IntPtr CreateItemPointerForMonitor(IntPtr hMonitor)
    {
        // The interop interface is reached by casting the projected class itself; no activation
        // factory call is needed. Method order on this interface is vtable-significant and comes
        // from windows.graphics.capture.interop.h: CreateForWindow is slot 0 and CreateForMonitor
        // is slot 1. Swapping them would call the wrong native method rather than failing to build.
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemPointer = interop.CreateForMonitor(hMonitor, GraphicsCaptureItemId);

        if (itemPointer == IntPtr.Zero)
        {
            // A failing HRESULT surfaces as a COMException from the call itself; this guards the
            // residual "success but null" case so the caller never gets a null item.
            throw new InvalidOperationException(
                $"CreateForMonitor returned no capture item for monitor handle 0x{hMonitor:X}.");
        }

        return itemPointer;
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        var result = NativeMethods.D3D11CreateDevice(
            IntPtr.Zero,
            NativeMethods.D3D_DRIVER_TYPE_HARDWARE,
            IntPtr.Zero,
            NativeMethods.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            IntPtr.Zero,
            0,
            NativeMethods.D3D11_SDK_VERSION,
            out var d3dDevice,
            out _,
            out var immediateContext);

        Marshal.ThrowExceptionForHR(result);

        try
        {
            var dxgiDeviceId = NativeMethods.IID_IDXGIDevice;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, ref dxgiDeviceId, out var dxgiDevice));

            try
            {
                Marshal.ThrowExceptionForHR(
                    NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));

                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    _ = Marshal.Release(inspectable);
                }
            }
            finally
            {
                _ = Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            _ = Marshal.Release(d3dDevice);

            if (immediateContext != IntPtr.Zero)
            {
                _ = Marshal.Release(immediateContext);
            }
        }
    }

    /// <summary>
    /// Interop view of <c>GraphicsCaptureItem</c>: the only way to obtain a capture item for a
    /// monitor without putting a picker dialog in front of the user.
    /// </summary>
    [ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [ComVisible(true)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, in Guid iid);

        IntPtr CreateForMonitor([In] IntPtr monitor, in Guid iid);
    }
}
