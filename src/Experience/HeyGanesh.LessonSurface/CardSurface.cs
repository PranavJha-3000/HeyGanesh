using System.Collections.Concurrent;
using System.Text.Json;
using HeyGanesh.Abstractions;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;
using Microsoft.Web.WebView2.Core;

namespace HeyGanesh.LessonSurface;

/// <summary>
/// The real <see cref="ILessonCanvas"/>: lesson cards on screen (DEC-020). One shared WebView2
/// environment is created up front (spike 0a: ~0.8 s cold, ~2.5 ms per beat warm) with one card
/// pre-warmed; each declared surface gets a card, laid out top-right on the monitor under the cursor.
/// Screen marks go to the native ScreenOverlay. Only validated plans reach this class (DEC-023), so
/// every id it is given exists — but every string is still treated as untrusted text by the page.
/// After a 3D lesson it can also let the student click a part (<see cref="IPartPicker"/>, DEC-031).
/// </summary>
public sealed class CardSurface : ILessonCanvas, IPartPicker, ISchematicCanvas, IAsyncDisposable
{
    private readonly UiThread _ui = new();
    private readonly CardSurfaceOptions _options;
    private readonly ScreenOverlayService? _marks;
    private readonly ConcurrentQueue<string> _markFaults = new();
    private readonly SemaphoreSlim _markSwitch = new(1, 1);
    private readonly object _cardsLock = new();

    // UI-thread state.
    private readonly List<CardWindow> _cards = new();
    private readonly Dictionary<string, string> _owner = new(StringComparer.Ordinal);

    // The 3D scene window (DEC-030): created on the first lesson that declares a scene, kept for this
    // surface's lifetime, hidden when a lesson has none. Ids it owns (instances, labels) route ops to it.
    private CardWindow? _scene;
    private string? _sceneSurfaceId;
    private readonly HashSet<string> _sceneOwned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sceneInstances = new(StringComparer.Ordinal);

    /// <summary>
    /// The current lesson's schematics as drawable models (DEC-050), replaced as a whole by each play. Set
    /// from the player's thread and read on the UI thread, so it is only ever swapped, never mutated.
    /// </summary>
    private volatile IReadOnlyDictionary<string, PartLibrary.LibraryModel> _schematics = new Dictionary<string, PartLibrary.LibraryModel>(StringComparer.Ordinal);
    // Picking (DEC-031), UI-thread state: the one wait in flight, if any. A click is accepted only
    // while it exists and only for an instance this surface drew and a part the catalogue has.
    private TaskCompletionSource<PartPick?>? _pickWait;
    private int _refusedPicks;
    private CoreWebView2Environment? _environment;
    private string _pageFolder = string.Empty;
    private int _seq;

    private Task _currentMark = Task.CompletedTask;
    private volatile bool _everFocused;
    private int _disposed;

    private CardSurface(CardSurfaceOptions options, ScreenOverlayService? marks)
    {
        _options = options;
        _marks = marks;
    }

    /// <summary>Raised when the student closes the cards with a card's × button.</summary>
    public event EventHandler? DismissedByUser;

    /// <summary>
    /// True if any card was ever seen as the foreground window — the one that receives the student's
    /// keyboard. It must never be: cards are WS_EX_NOACTIVATE.
    /// </summary>
    public bool EverTookFocus => _everFocused;

    /// <summary>
    /// Diagnostics only: WinForms raised Activated on a card. That is thread-local activation inside
    /// our own UI thread (WebView2 moving focus into its child), distinct from becoming foreground.
    /// </summary>
    public bool ActivationSeen
    {
        get
        {
            lock (_cardsLock)
            {
                return _cards.Any(c => c.TookFocus) || (_scene?.TookFocus ?? false);
            }
        }
    }

    /// <summary>
    /// The 3D vocabulary this canvas can draw: pass it as <c>ValidationContext.Scene3d</c> when planning
    /// for this surface (DEC-028). Every model, part and animation in it is in the Part Library.
    /// </summary>
    public static ModelCatalog Scene3dCatalog => PartLibrary.ModelLibrary.ToCatalog();

    /// <summary>Diagnostics: how long the last scene window took to start (environment page + Babylon parse + shader warm-up).</summary>
    public TimeSpan LastSceneStartup { get; private set; }

    /// <summary>Diagnostics: part clicks refused — no pick was asked for, or the ids were not ones this surface drew.</summary>
    public int RefusedPicks => Volatile.Read(ref _refusedPicks);

    /// <summary>Marks the overlay could not show, as short reasons (no screen text, no coordinates).</summary>
    public IReadOnlyList<string> MarkFaults => _markFaults.ToArray();

    public static async Task<CardSurface> CreateAsync(CardSurfaceOptions? options = null, ScreenOverlayService? marks = null, CancellationToken cancellationToken = default)
    {
        var surface = new CardSurface(options ?? CardSurfaceOptions.Default, marks);
        try
        {
            await surface._ui.InvokeAsync(surface.StartAsync).WaitAsync(cancellationToken);
            return surface;
        }
        catch
        {
            await surface.DisposeAsync();
            throw;
        }
    }

    public Task ResetAsync(IReadOnlyList<VisualLessonEngine.LessonSurface> surfaces, CancellationToken cancellationToken) => OnUi(async () =>
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        _owner.Clear();
        _sceneOwned.Clear();
        _sceneInstances.Clear();
        var cardSurfaces = surfaces.Where(s => s.Kind == SurfaceKind.Card).ToList();
        var sceneSurface = surfaces.FirstOrDefault(s => s.Kind == SurfaceKind.Scene3d);
        _sceneSurfaceId = sceneSurface?.Id;
        while (CardCount() < cardSurfaces.Count)
        {
            await NewCardAsync();
        }

        var cards = Snapshot();
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].SurfaceId = i < cardSurfaces.Count ? cardSurfaces[i].Id : string.Empty;
        }

        await Task.WhenAll(cardSurfaces.Select((s, i) => SendAsync(cards[i], "reset", seq => new { kind = "reset", seq, title = s.Title }, cancellationToken)));
        CardWindow? scene = null;
        if (sceneSurface is not null)
        {
            scene = await EnsureSceneAsync(cancellationToken);
            scene.SurfaceId = sceneSurface.Id;
            await SendAsync(scene, "reset", seq => new { kind = "reset", seq, title = sceneSurface.Title }, cancellationToken);
        }
        else
        {
            _scene?.Hide();
        }

        Arrange(cards.Take(cardSurfaces.Count).ToArray(), scene);
        foreach (var hidden in cards.Skip(cardSurfaces.Count))
        {
            hidden.Hide();
        }
    });

    public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken) => OnUi(async () =>
    {
        ArgumentNullException.ThrowIfNull(beat);
        var perCard = new Dictionary<string, List<object>>(StringComparer.Ordinal);
        var sceneOps = new List<object>();
        void Add(string surface, object op)
        {
            if (!perCard.TryGetValue(surface, out var list))
            {
                perCard[surface] = list = new List<object>();
            }

            list.Add(op);
        }

        foreach (var op in beat.Ops)
        {
            switch (op)
            {
                case WriteOp w:
                    _owner[w.Id] = w.Surface;
                    Add(w.Surface, new { op = "write", id = w.Id, text = w.Text, style = w.Style.ToString().ToLowerInvariant(), math = w.Math });
                    break;
                case StepOp s:
                    _owner[s.Id] = s.Surface;
                    Add(s.Surface, new { op = "step", id = s.Id, index = s.Index, text = s.Text });
                    break;
                case IconOp i:
                    _owner[i.Id] = i.Surface;
                    Add(i.Surface, new { op = "icon", id = i.Id, glyph = i.Glyph });
                    break;
                case PlotOp p:
                    _owner[p.Id] = p.Surface;
                    // Points are computed here from the validated expression — the page only draws them.
                    var points = PlotExpression.Sample(p).Select(pt => pt is null ? null : new[] { pt.X, pt.Y }).ToArray();
                    Add(p.Surface, new { op = "plot", id = p.Id, xMin = p.XMin, xMax = p.XMax, points });
                    break;
                case ArrowOp a:
                    Add(a.Surface, new { op = "arrow", from = a.From, to = a.To, label = a.Label });
                    break;
                case HighlightOp h when _owner.TryGetValue(h.Target, out var holder):
                    // The highlight goes to whichever card holds the element, not the beat's card.
                    Add(holder, new { op = "highlight", target = h.Target });
                    break;
                case HighlightOp sh when IsSceneTarget(sh.Target):
                    sceneOps.Add(SceneMessages.Highlight(sh));
                    break;
                case ModelOp model:
                    _sceneOwned.Add(model.Id);
                    _sceneInstances[model.Id] = model.Model;
                    // The lesson's own schematic first (DEC-050), then the library; the validator allowed nothing else.
                    sceneOps.Add(_schematics.TryGetValue(model.Model, out var schematic) ? SceneMessages.Model(model, schematic) : SceneMessages.Model(model));
                    break;
                case LabelOp label:
                    _sceneOwned.Add(label.Id);
                    sceneOps.Add(SceneMessages.Label(label));
                    break;
                case ExplodeOp explode:
                    sceneOps.Add(SceneMessages.Explode(explode));
                    break;
                case AnimateOp animate:
                    sceneOps.Add(SceneMessages.Animation(animate, _sceneInstances[animate.Target]));
                    break;
                case CameraOp camera:
                    sceneOps.Add(SceneMessages.Camera(camera));
                    break;
                case ClearOp sc when sc.Surface == _sceneSurfaceId:
                    _sceneOwned.Clear();
                    _sceneInstances.Clear();
                    sceneOps.Add(SceneMessages.Clear());
                    break;
                case ClearOp c:
                    foreach (var gone in _owner.Where(kv => kv.Value == c.Surface).Select(kv => kv.Key).ToList())
                    {
                        _owner.Remove(gone);
                    }

                    Add(c.Surface, new { op = "clear" });
                    break;
                case ScreenMarkOp m:
                    ShowMark(m);
                    break;
            }
        }

        var cards = Snapshot();
        var sends = perCard.Select(kv =>
        {
            var card = cards.FirstOrDefault(c => c.SurfaceId == kv.Key)
                ?? throw new InvalidOperationException($"beat '{beat.Id}' draws on surface '{kv.Key}', which was not laid out by ResetAsync");
            return SendAsync(card, "beat", seq => new { kind = "beat", seq, ops = kv.Value }, cancellationToken);
        }).ToList();
        if (sceneOps.Count > 0)
        {
            var scene = _scene is not null && _sceneSurfaceId is not null
                ? _scene
                : throw new InvalidOperationException($"beat '{beat.Id}' draws on a 3D scene, which was not laid out by ResetAsync");
            sends.Add(SendAsync(scene, "beat", seq => new { kind = "beat", seq, ops = sceneOps }, cancellationToken));
        }

        await Task.WhenAll(sends);
        NoteFocus(AllWindows());
    });

    public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken) => OnUi(async () =>
    {
        _owner.Clear();
        if (CardCount() == 0)
        {
            await NewCardAsync();
        }

        var cards = Snapshot();
        cards[0].SurfaceId = "notice";
        await SendAsync(cards[0], "reset", seq => new { kind = "reset", seq, title = "heyganesh" }, cancellationToken);
        await SendAsync(cards[0], "notice", seq => new { kind = "notice", seq, text = notice }, cancellationToken);
        _scene?.Hide();
        Arrange(new[] { cards[0] });
        foreach (var hidden in cards.Skip(1))
        {
            hidden.Hide();
        }
    });

    /// <summary>
    /// Lets the student click one part of the scene on show (DEC-031). The page shows a hint and posts
    /// only ids; <see cref="OnPartClicked"/> checks them. Null when there is no scene with a model on
    /// show, no WebGL, the student closed the lesson, or <paramref name="idle"/> passed.
    /// </summary>
    public async Task<PartPick?> PickAsync(TimeSpan idle, CancellationToken cancellationToken)
    {
        var wait = await OnUi(async () =>
        {
            if (_scene is null || !_scene.Visible || _sceneInstances.Count == 0)
            {
                return null;
            }

            var reply = await SendAsync(_scene, "picking", seq => new { kind = "picking", seq, on = true }, cancellationToken);
            if (reply.TryGetProperty("skipped", out _))
            {
                return null;   // no WebGL: nothing was drawn, so there is nothing to click
            }

            _pickWait = new TaskCompletionSource<PartPick?>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pickWait;
        });

        if (wait is null)
        {
            return null;
        }

        try
        {
            return await wait.Task.WaitAsync(idle, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            try
            {
                await OnUi(async () =>
                {
                    if (ReferenceEquals(_pickWait, wait))
                    {
                        _pickWait = null;
                    }

                    if (_scene is not null)
                    {
                        await SendAsync(_scene, "picking", seq => new { kind = "picking", seq, on = false }, CancellationToken.None);
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Disposed while waiting: the wait already ended with null and there is no page to tell.
            }
        }
    }

    /// <summary>Hides every card and any mark. Cards are reused by the next lesson.</summary>
    public async Task HideAsync()
    {
        await OnUi(() =>
        {
            foreach (var card in AllWindows())
            {
                card.Hide();
            }

            return Task.CompletedTask;
        });

        if (_marks is not null && _marks.IsShowing)
        {
            await _marks.HideAsync();
        }
    }

    public Task<CardSnapshot> DescribeCardAsync(string surfaceId) => OnUi(async () =>
    {
        var card = Snapshot().FirstOrDefault(c => c.SurfaceId == surfaceId)
            ?? throw new ArgumentException($"no card is laid out for surface '{surfaceId}'", nameof(surfaceId));
        // ExecuteScriptAsync returns the script's value JSON-encoded; the page returns a JSON string.
        var raw = await card.RunHostScriptAsync("JSON.stringify(window.__describe())");
        using var outer = JsonDocument.Parse(raw);
        using var doc = JsonDocument.Parse(outer.RootElement.GetString()!);
        var d = doc.RootElement;
        CardNative.GetWindowDisplayAffinity(card.Handle, out var affinity);
        var b = card.Bounds;
        return new CardSnapshot(
            surfaceId,
            d.GetProperty("title").GetString() ?? string.Empty,
            card.Visible,
            CardNative.GetWindowLong(card.Handle, CardNative.GwlExStyle),
            affinity,
            new PixelRect(b.X, b.Y, b.Width, b.Height),
            d.GetProperty("ids").EnumerateArray().Select(e => e.GetString()!).ToList(),
            d.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal),
            d.GetProperty("arrows").GetInt32(),
            d.GetProperty("highlights").EnumerateArray().Select(e => e.GetString()!).ToList(),
            d.GetProperty("plotPoints").GetInt32(),
            d.GetProperty("img").GetInt32(),
            d.GetProperty("script").GetInt32(),
            d.GetProperty("bold").GetInt32(),
            d.GetProperty("notice").GetString() ?? string.Empty,
            card.BrowserAcceleratorKeysEnabled,
            CardNative.DwmGetWindowAttribute(card.Handle, CardNative.DwmwaWindowCornerPreference, out var corner, sizeof(int)) == 0 ? corner : -1);
    });

    /// <summary>Writes one card's page as PNG to <paramref name="destination"/> (review / lesson history).</summary>
    public Task CapturePageAsync(string surfaceId, Stream destination) => OnUi(async () =>
    {
        var card = AllWindows().FirstOrDefault(c => c.SurfaceId == surfaceId)
            ?? throw new ArgumentException($"no card is laid out for surface '{surfaceId}'", nameof(surfaceId));
        await card.CapturePageAsync(destination);
    });

    /// <summary>Read-only description of the 3D scene window and its page (DEC-030), for verification rows.</summary>
    public Task<SceneSnapshot> DescribeSceneAsync() => OnUi(async () =>
    {
        var scene = _scene ?? throw new InvalidOperationException("no scene window has been created");
        var raw = await scene.RunHostScriptAsync("JSON.stringify(window.__describe())");
        using var outer = JsonDocument.Parse(raw);
        using var doc = JsonDocument.Parse(outer.RootElement.GetString()!);
        var d = doc.RootElement;
        CardNative.GetWindowDisplayAffinity(scene.Handle, out var affinity);
        var b = scene.Bounds;
        static List<string> Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();

        // The page reports viewport CSS pixels; the window's client area is the whole window (borderless,
        // WebView2 docked), so screen = window origin + CSS x the window's DPI scale.
        var scale = scene.DeviceDpi / 96.0;
        Point ToScreen(JsonElement xy) => new(b.X + (int)Math.Round(xy[0].GetDouble() * scale), b.Y + (int)Math.Round(xy[1].GetDouble() * scale));
        var drawing = d.GetProperty("drawing");
        var drawn = drawing.ValueKind == JsonValueKind.Object;
        return new SceneSnapshot(
            scene.SurfaceId,
            scene.Visible,
            CardNative.GetWindowLong(scene.Handle, CardNative.GwlExStyle),
            affinity,
            new PixelRect(b.X, b.Y, b.Width, b.Height),
            d.GetProperty("webgl").GetInt32(),
            d.GetProperty("title").GetString() ?? string.Empty,
            Strings(d.GetProperty("tags")),
            d.GetProperty("notice").GetString() ?? string.Empty,
            d.GetProperty("instances").EnumerateArray()
                .Select(i => new SceneInstance(i.GetProperty("id").GetString()!, i.GetProperty("model").GetString()!, i.GetProperty("parts").GetInt32(), i.GetProperty("meshes").GetInt32()))
                .ToList(),
            d.GetProperty("labels").EnumerateArray()
                .Select(l => new SceneLabel(l.GetProperty("id").GetString()!, l.GetProperty("target").GetString()!, l.GetProperty("text").GetString()!))
                .ToList(),
            Strings(d.GetProperty("highlights")),
            d.GetProperty("explode").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal),
            d.GetProperty("offsets").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(x => x.GetDouble()).ToArray(), StringComparer.Ordinal),
            Strings(d.GetProperty("playing")),
            d.GetProperty("cameraView").GetString() ?? string.Empty,
            d.GetProperty("cameraFocus").GetString() ?? string.Empty,
            d.GetProperty("frames").GetInt32(),
            d.GetProperty("csp").GetInt32(),
            d.GetProperty("lastBuildMs").GetDouble(),
            d.GetProperty("script").GetInt32(),
            d.GetProperty("picking").GetBoolean(),
            d.GetProperty("pickPoints").EnumerateObject().ToDictionary(p => p.Name, p => ToScreen(p.Value), StringComparer.Ordinal),
            ToScreen(d.GetProperty("closeAt")),
            drawn ? drawing.GetProperty("solids").GetInt32() : null,
            drawn ? drawing.GetProperty("buildMs").GetDouble() : 0,
            drawn
                ? new SceneDrawing(
                    Strings(drawing.GetProperty("order")),
                    drawing.GetProperty("lit").GetInt32(),
                    drawing.GetProperty("hash").GetInt64(),
                    Strings(drawing.GetProperty("labelTexts")),
                    drawing.GetProperty("labelForeign").GetInt32())
                : null);
    });

    /// <summary>
    /// Orientation self-test (DEC-029 asked for it, DEC-030 automates it): the scene page draws a red
    /// marker at +X, a green one at +Y, and a blue one on a parent turned 90° about Z by a quaternion
    /// computed here — so it must land at −X. The page is then captured and the markers are found by
    /// colour in its own pixels. Usable as a startup health check; it leaves the scene cleared. Without
    /// WebGL the page draws the same three markers in its SVG drawing (DEC-034), so the probe checks that path too.
    /// </summary>
    public Task<OrientationProbe> CheckOrientationAsync() => OnUi(async () =>
    {
        var scene = await EnsureSceneAsync(CancellationToken.None);
        var wasVisible = scene.Visible;
        if (!wasVisible)
        {
            Arrange(Array.Empty<CardWindow>(), scene);
        }

        try
        {
            var reply = await SendAsync(scene, "orient", seq => new { kind = "orient", seq, turn = SceneMessages.OrientationTurn }, CancellationToken.None, _options.ControlTimeout);
            if (reply.TryGetProperty("skipped", out _))
            {
                return new OrientationProbe(false, false, false, "no WebGL: nothing was drawn");
            }

            using var png = new MemoryStream();
            await scene.CapturePageAsync(png);
            png.Position = 0;
            using var bitmap = new Bitmap(png);
            var red = Centroid(bitmap, (r, g, b) => r > 200 && g < 90 && b < 90);
            var green = Centroid(bitmap, (r, g, b) => g > 180 && r < 90 && b < 90);
            var blue = Centroid(bitmap, (r, g, b) => b > 200 && r < 90 && g < 120);
            double cx = bitmap.Width / 2.0, cy = bitmap.Height / 2.0;
            var tol = bitmap.Width * 0.05;
            var detail = $"image {bitmap.Width}x{bitmap.Height}; red {Show(red)}, green {Show(green)}, blue {Show(blue)}";
            return new OrientationProbe(
                red is { } rp && rp.X > cx + tol && Math.Abs(rp.Y - cy) < tol,
                green is { } gp && gp.Y < cy - tol && Math.Abs(gp.X - cx) < tol,
                blue is { } bp && bp.X < cx - tol && Math.Abs(bp.Y - cy) < tol,
                detail);
        }
        finally
        {
            await SendAsync(scene, "reset", seq => new { kind = "reset", seq, title = string.Empty }, CancellationToken.None);
            if (!wasVisible)
            {
                scene.Hide();
            }
        }
    });

    /// <summary>
    /// Self-test of the pick guard (DEC-031): makes the scene page post three forged clicks — an instance
    /// this surface never drew, a real instance with a part its model lacks, and ids that are not
    /// strings — and returns how many were refused. All three must be, and a pick in progress must still
    /// be waiting afterwards. The script is fixed; the one id in it is a validated instance id, JSON-encoded.
    /// </summary>
    public Task<int> ProbePickGuardAsync() => OnUi(async () =>
    {
        var scene = _scene ?? throw new InvalidOperationException("no scene window has been created");
        var instance = JsonSerializer.Serialize(_sceneInstances.Keys.FirstOrDefault() ?? "none");
        var before = RefusedPicks;
        await scene.RunHostScriptAsync(
            "window.chrome.webview.postMessage({ kind: 'part-clicked', instance: 'ghost', part: 'piston' });"
            + $"window.chrome.webview.postMessage({{ kind: 'part-clicked', instance: {instance}, part: 'flux-capacitor' }});"
            + "window.chrome.webview.postMessage({ kind: 'part-clicked', instance: 42, part: null }); 1");
        await Task.Delay(400);
        return RefusedPicks - before;
    });

    /// <summary>
    /// Self-test of the page's isolation: tries to navigate away and to fetch an outside URL from
    /// inside the page, and reports whether both were refused. Usable as a startup health check.
    /// </summary>
    public Task<IsolationProbe> ProbeIsolationAsync(string surfaceId) => OnUi(async () =>
    {
        var card = Snapshot().First(c => c.SurfaceId == surfaceId);
        var before = card.BlockedNavigations;
        await card.RunHostScriptAsync("location.href = 'https://example.com/'; 1");
        await Task.Delay(400);
        var origin = await card.RunHostScriptAsync("location.origin");
        var navigationBlocked = card.BlockedNavigations > before && origin == JsonSerializer.Serialize(CardWindow.PageOrigin);
        var reply = await SendAsync(card, "probe", seq => new { kind = "probe", seq }, CancellationToken.None, TimeSpan.FromSeconds(5));
        var fetchBlocked = reply.TryGetProperty("fetch", out var f) && f.GetString() == "blocked";
        return new IsolationProbe(navigationBlocked, fetchBlocked);
    });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _ui.InvokeAsync(() =>
            {
                _pickWait?.TrySetResult(null);
                foreach (var card in AllWindows())
                {
                    card.FailPending(new ObjectDisposedException(nameof(CardSurface)));
                    card.Close();
                    card.Dispose();
                }

                lock (_cardsLock)
                {
                    _cards.Clear();
                    _scene = null;
                }

                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or TimeoutException)
        {
            // The UI thread is already gone or wedged; tearing it down below is all that is left.
        }

        _ui.Dispose();
        _markSwitch.Dispose();
    }

    private async Task StartAsync()
    {
        _pageFolder = Path.Combine(AppContext.BaseDirectory, "LessonPage");
        if (!File.Exists(Path.Combine(_pageFolder, "lesson.html")))
        {
            throw new FileNotFoundException("the lesson page was not deployed next to the host", Path.Combine(_pageFolder, "lesson.html"));
        }

        var dataRoot = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        var userData = _options.UserDataFolder
            ?? Path.Combine(string.IsNullOrEmpty(dataRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh") : dataRoot, "webview2");
        var environmentOptions = string.IsNullOrWhiteSpace(_options.AdditionalBrowserArguments)
            ? null
            : new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = _options.AdditionalBrowserArguments };
        _environment = await CoreWebView2Environment.CreateAsync(null, userData, environmentOptions);
        await NewCardAsync();
    }

    /// <summary>
    /// The scene window, created on first use (DEC-030: the 360–500 MB a 3D page costs is paid only by
    /// lessons that show one). Creation includes parsing the vendored Babylon.js and a shader warm-up —
    /// one hidden mesh per shape × finish, rendered once — so the lesson's real model builds warm.
    /// </summary>
    private async Task<CardWindow> EnsureSceneAsync(CancellationToken cancellationToken)
    {
        if (_scene is not null)
        {
            return _scene;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var scene = new CardWindow();
        scene.CloseRequested += (_, _) =>
        {
            foreach (var c in AllWindows())
            {
                c.Hide();
            }

            _pickWait?.TrySetResult(null);
            DismissedByUser?.Invoke(this, EventArgs.Empty);
        };
        scene.PartClicked += OnPartClicked;
        await scene.InitializeAsync(_environment!, _pageFolder, CardWindow.ScenePage);
        lock (_cardsLock)
        {
            _scene = scene;
        }

        await SendAsync(scene, "warmup", seq => new { kind = "warmup", seq }, cancellationToken);
        LastSceneStartup = clock.Elapsed;
        return scene;
    }

    /// <summary>
    /// UI thread. The page's ids are not trusted: the instance must be one this surface drew, its model
    /// must be in the catalogue or be the lesson's own schematic (DEC-057), the part must be one of that
    /// model's, and a pick must be in progress. Anything else is counted and dropped, so a confused or
    /// scripted page can never start a question.
    /// </summary>
    private void OnPartClicked(string? instance, string? part)
    {
        if (_pickWait is { } wait
            && instance is not null
            && part is not null
            && _sceneInstances.TryGetValue(instance, out var model)
            && ((Scene3dCatalog.TryGet(model, out var entry) && entry.Parts.Contains(part))
                || (_schematics.TryGetValue(model, out var schematic) && schematic.Parts.Any(p => p.Id == part))))
        {
            _pickWait = null;
            wait.TrySetResult(new PartPick(instance, model, part));
            return;
        }

        Interlocked.Increment(ref _refusedPicks);
    }

    /// <summary>
    /// The lesson's schematics, converted once per play. Since DEC-057 a click on a schematic's part is
    /// accepted like a library part's (<see cref="OnPartClicked"/>); the follow-up question for it is written
    /// by <c>PartQuestion</c> from the schematic's cleaned names, and says the part is from a simplified drawing.
    /// </summary>
    public void UseSchematics(IReadOnlyList<SchematicModel> schematics)
    {
        ArgumentNullException.ThrowIfNull(schematics);
        _schematics = schematics.ToDictionary(s => s.Id, s => PartLibrary.Schematics.ToModel(s), StringComparer.Ordinal);
    }

    private bool IsSceneTarget(string target) => target.Contains('.', StringComparison.Ordinal) || _sceneOwned.Contains(target);

    private CardWindow[] AllWindows()
    {
        lock (_cardsLock)
        {
            return _scene is null ? _cards.ToArray() : _cards.Append(_scene).ToArray();
        }
    }

    private static PointF? Centroid(Bitmap bitmap, Func<int, int, int, bool> match)
    {
        double sx = 0, sy = 0;
        var n = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                if (match(c.R, c.G, c.B))
                {
                    sx += x;
                    sy += y;
                    n++;
                }
            }
        }

        return n < 20 ? null : new PointF((float)(sx / n), (float)(sy / n));
    }

    private static string Show(PointF? p) => p is { } v ? $"({v.X:F0},{v.Y:F0})" : "not found";

    private async Task<CardWindow> NewCardAsync()
    {
        var card = new CardWindow();
        card.CloseRequested += (_, _) =>
        {
            foreach (var c in Snapshot())
            {
                c.Hide();
            }

            _pickWait?.TrySetResult(null);
            DismissedByUser?.Invoke(this, EventArgs.Empty);
        };
        await card.InitializeAsync(_environment!, _pageFolder);
        lock (_cardsLock)
        {
            _cards.Add(card);
        }

        return card;
    }

    /// <summary>Beats get <see cref="CardSurfaceOptions.ApplyTimeout"/>; layout/notice messages get the control budget.</summary>
    private Task<JsonElement> SendAsync(CardWindow card, string kind, Func<int, object> build, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var seq = ++_seq;
        var budget = timeout ?? (kind == "beat" ? _options.ApplyTimeout : _options.ControlTimeout);
        return card.SendAsync(seq, kind, build(seq), budget, cancellationToken);
    }

    /// <summary>
    /// Top-right of the monitor under the cursor, in a grid that fits its working area. A scene window
    /// goes to the left of the card column (narrowed to fit, never below 320 DIP wide).
    /// </summary>
    private void Arrange(CardWindow[] cards, CardWindow? scene = null)
    {
        if (cards.Length == 0 && scene is null)
        {
            return;
        }

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var scale = (cards.Length > 0 ? cards[0] : scene!).DeviceDpi / 96.0;
        if (cards.Length == 0)
        {
            PlaceScene(scene!, area.Right - (int)Math.Round(_options.MarginDip * scale) + (int)Math.Round(_options.GapDip * scale), area, scale);
            NoteFocus(new[] { scene! });
            return;
        }
        int S(int dip) => (int)Math.Round(dip * scale);
        int w = S(_options.CardWidthDip), h = S(_options.CardHeightDip), gap = S(_options.GapDip), m = S(_options.MarginDip);
        var cols = Math.Clamp((area.Width - 2 * m + gap) / (w + gap), 1, cards.Length);
        var width = Math.Min(w, area.Width - 2 * m);
        // Rows must fit the working area too (a 1080p laptop at 125-150% cannot stack two full-height
        // rows): shrink the card height; the page zooms its content to fit whatever height it gets.
        var rows = (cards.Length + cols - 1) / cols;
        h = Math.Max(S(160), Math.Min(h, (area.Height - 2 * m - (rows - 1) * gap) / rows));
        var x0 = area.Right - m - cols * width - (cols - 1) * gap;
        for (var i = 0; i < cards.Length; i++)
        {
            int col = i % cols, row = i / cols;
            cards[i].Bounds = new Rectangle(x0 + col * (width + gap), area.Top + m + row * (h + gap), width, h);
            if (!cards[i].Visible)
            {
                cards[i].Show();
            }
        }

        if (scene is not null)
        {
            PlaceScene(scene, cards.Min(c => c.Bounds.Left), area, scale);
            NoteFocus(new[] { scene });
        }

        NoteFocus(cards);
    }

    /// <summary>The scene window, just left of <paramref name="columnLeft"/>, top-aligned with the cards.</summary>
    private void PlaceScene(CardWindow scene, int columnLeft, Rectangle area, double scale)
    {
        int D(int dip) => (int)Math.Round(dip * scale);
        var room = columnLeft - D(_options.GapDip) - (area.Left + D(_options.MarginDip));
        var width = Math.Max(D(320), Math.Min(D(_options.SceneWidthDip), room));
        var height = Math.Min(D(_options.SceneHeightDip), area.Height - 2 * D(_options.MarginDip));
        scene.Bounds = new Rectangle(columnLeft - D(_options.GapDip) - width, area.Top + D(_options.MarginDip), width, height);
        if (!scene.Visible)
        {
            scene.Show();
        }
    }

    private void NoteFocus(IEnumerable<CardWindow> cards)
    {
        var foreground = CardNative.GetForegroundWindow();
        if (cards.Any(c => c.Handle == foreground))
        {
            _everFocused = true;
        }
    }

    /// <summary>
    /// Shows one mark on the native overlay without holding up the beat. The overlay refuses a second
    /// show while one is up, so a new mark first hides the previous one and waits for it to end.
    /// </summary>
    private void ShowMark(ScreenMarkOp mark)
    {
        if (_marks is null)
        {
            _markFaults.Enqueue("no overlay attached");
            return;
        }

        var r = mark.Region;
        var guidance = new GuidanceResult
        {
            Found = true,
            Description = "lesson mark",
            BoxOnScreen = r,
            CenterOnScreen = new GuidancePoint(r.X + r.Width / 2, r.Y + r.Height / 2),
            Confidence = GuidanceConfidence.High,
        };

        _ = Task.Run(async () =>
        {
            await _markSwitch.WaitAsync();
            try
            {
                if (_marks.IsShowing)
                {
                    await _marks.HideAsync();
                }

                await _currentMark.ContinueWith(_ => { }, TaskScheduler.Default);
                _currentMark = ObserveAsync(_marks.ShowAsync(guidance));
            }
            catch (Exception ex)
            {
                _markFaults.Enqueue(ex.GetType().Name);
            }
            finally
            {
                _markSwitch.Release();
            }
        });
    }

    private async Task ObserveAsync(Task<OverlayShowResult> show)
    {
        try
        {
            var result = await show;
            if (!result.Displayed && result.Reason != OverlayShowResult.ReasonCancelled)
            {
                _markFaults.Enqueue(result.Reason ?? "not displayed");
            }
        }
        catch (Exception ex)
        {
            _markFaults.Enqueue(ex.GetType().Name);
        }
    }

    private Task OnUi(Func<Task> work) => OnUi<bool>(async () =>
    {
        await work();
        return true;
    });

    private Task<T> OnUi<T>(Func<Task<T>> work)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _ui.InvokeAsync(work);
    }

    private int CardCount()
    {
        lock (_cardsLock)
        {
            return _cards.Count;
        }
    }

    private CardWindow[] Snapshot()
    {
        lock (_cardsLock)
        {
            return _cards.ToArray();
        }
    }
}
