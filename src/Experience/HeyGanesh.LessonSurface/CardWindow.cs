using System.Collections.Concurrent;
using System.Text.Json;
using HeyGanesh.ScreenCapture;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HeyGanesh.LessonSurface;

/// <summary>
/// One lesson card: a borderless, top-most window that never takes focus and is invisible to our own
/// screen capture, hosting the lesson page. Lives on the surface's UI thread; every member except
/// the acknowledgement plumbing must be called there.
/// </summary>
internal sealed class CardWindow : Form
{
    public const string PageOrigin = "https://lesson.heyganesh.invalid";
    private const string PageUrl = PageOrigin + "/lesson.html";

    private readonly WebView2 _web;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private TaskCompletionSource? _ready;
    private int _blockedNavigations;

    public CardWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0xf6, 0xf1, 0xe3);
        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(_web);
    }

    public string SurfaceId { get; set; } = string.Empty;

    public bool TookFocus { get; private set; }

    public int BlockedNavigations => Volatile.Read(ref _blockedNavigations);

    /// <summary>Raised (UI thread) when the student presses the card's close button.</summary>
    public event EventHandler? CloseRequested;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= CardNative.WsExNoActivate | CardNative.WsExToolWindow | CardNative.WsExTopmost;
            return cp;
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        // WS_EX_NOACTIVATE should make this unreachable; recorded so LSU-2 can prove it.
        TookFocus = true;
        base.OnActivated(e);
    }

    /// <summary>
    /// Creates the HWND, excludes it from capture (a hard gate — the capture module must never
    /// photograph our own lesson), and loads the page from the virtual host.
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment, string pageFolder)
    {
        CreateControl();
        if (!CaptureExclusion.TryExcludeFromCapture(Handle, out var error))
        {
            throw new InvalidOperationException($"card window could not be excluded from capture (Win32 error {error})");
        }

        await _web.EnsureCoreWebView2Async(environment);
        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsWebMessageEnabled = true;
        core.SetVirtualHostNameToFolderMapping("lesson.heyganesh.invalid", pageFolder, CoreWebView2HostResourceAccessKind.Deny);

        // The page may only ever be the lesson page: anything else (a link, a script setting
        // location, a redirect) is cancelled, and new windows are refused outright.
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(PageOrigin + "/", StringComparison.Ordinal))
            {
                e.Cancel = true;
                Interlocked.Increment(ref _blockedNavigations);
            }
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += OnMessage;

        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core.Navigate(PageUrl);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>Posts one message and waits for the page's matching "done" (or the timeout).</summary>
    public async Task<JsonElement> SendAsync(int seq, object message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;
        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            var reply = await tcs.Task.WaitAsync(timeout, cancellationToken);
            if (reply.TryGetProperty("error", out var error))
            {
                // The page caught its own exception and said so: a render fault, not a silent blank.
                throw new InvalidOperationException($"the lesson page could not render message {seq}: {error.GetString()}");
            }

            return reply;
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    /// <summary>
    /// Renders the page itself (not the screen) to PNG. Capture exclusion is about the desktop
    /// capture path; this is WebView2 drawing its own content, for review and lesson history.
    /// </summary>
    public Task CapturePageAsync(Stream destination) =>
        _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, destination);

    /// <summary>Runs one fixed, host-owned script. Never called with model text.</summary>
    public Task<string> RunHostScriptAsync(string script) => _web.CoreWebView2.ExecuteScriptAsync(script);

    /// <summary>Fails every in-flight send — the window is going away.</summary>
    public void FailPending(Exception reason)
    {
        foreach (var tcs in _pending.Values)
        {
            tcs.TrySetException(reason);
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement message;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            message = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        var kind = message.TryGetProperty("kind", out var k) ? k.GetString() : null;
        switch (kind)
        {
            case "ready":
                _ready?.TrySetResult();
                break;
            case "done" or "probe" when message.TryGetProperty("seq", out var s) && s.TryGetInt32(out var seq):
                if (_pending.TryGetValue(seq, out var tcs))
                {
                    tcs.TrySetResult(message);
                }

                break;
            case "drag":
                // Let the student move the card: hand the press to Windows as a caption drag.
                CardNative.ReleaseCapture();
                CardNative.SendMessage(Handle, CardNative.WmNcLButtonDown, CardNative.HtCaption, IntPtr.Zero);
                break;
            case "close":
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
