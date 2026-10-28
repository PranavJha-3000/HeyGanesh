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
/// </summary>
public sealed class CardSurface : ILessonCanvas, IAsyncDisposable
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
                return _cards.Any(c => c.TookFocus);
            }
        }
    }

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
        while (CardCount() < surfaces.Count)
        {
            await NewCardAsync();
        }

        var cards = Snapshot();
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].SurfaceId = i < surfaces.Count ? surfaces[i].Id : string.Empty;
        }

        await Task.WhenAll(surfaces.Select((s, i) => SendAsync(cards[i], seq => new { kind = "reset", seq, title = s.Title }, cancellationToken)));
        Arrange(cards.Take(surfaces.Count).ToArray());
        foreach (var hidden in cards.Skip(surfaces.Count))
        {
            hidden.Hide();
        }
    });

    public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken) => OnUi(async () =>
    {
        ArgumentNullException.ThrowIfNull(beat);
        var perCard = new Dictionary<string, List<object>>(StringComparer.Ordinal);
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
            return SendAsync(card, seq => new { kind = "beat", seq, ops = kv.Value }, cancellationToken);
        });
        await Task.WhenAll(sends);
        NoteFocus(cards);
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
        await SendAsync(cards[0], seq => new { kind = "reset", seq, title = "heyganesh" }, cancellationToken);
        await SendAsync(cards[0], seq => new { kind = "notice", seq, text = notice }, cancellationToken);
        Arrange(new[] { cards[0] });
        foreach (var hidden in cards.Skip(1))
        {
            hidden.Hide();
        }
    });

    /// <summary>Hides every card and any mark. Cards are reused by the next lesson.</summary>
    public async Task HideAsync()
    {
        await OnUi(() =>
        {
            foreach (var card in Snapshot())
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
            d.GetProperty("notice").GetString() ?? string.Empty);
    });

    /// <summary>Writes one card's page as PNG to <paramref name="destination"/> (review / lesson history).</summary>
    public Task CapturePageAsync(string surfaceId, Stream destination) => OnUi(async () =>
    {
        var card = Snapshot().FirstOrDefault(c => c.SurfaceId == surfaceId)
            ?? throw new ArgumentException($"no card is laid out for surface '{surfaceId}'", nameof(surfaceId));
        await card.CapturePageAsync(destination);
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
        var reply = await SendAsync(card, seq => new { kind = "probe", seq }, CancellationToken.None, TimeSpan.FromSeconds(5));
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
                foreach (var card in Snapshot())
                {
                    card.FailPending(new ObjectDisposedException(nameof(CardSurface)));
                    card.Close();
                    card.Dispose();
                }

                lock (_cardsLock)
                {
                    _cards.Clear();
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
        _environment = await CoreWebView2Environment.CreateAsync(null, userData);
        await NewCardAsync();
    }

    private async Task<CardWindow> NewCardAsync()
    {
        var card = new CardWindow();
        card.CloseRequested += (_, _) =>
        {
            foreach (var c in Snapshot())
            {
                c.Hide();
            }

            DismissedByUser?.Invoke(this, EventArgs.Empty);
        };
        await card.InitializeAsync(_environment!, _pageFolder);
        lock (_cardsLock)
        {
            _cards.Add(card);
        }

        return card;
    }

    private Task<JsonElement> SendAsync(CardWindow card, Func<int, object> build, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var seq = ++_seq;
        return card.SendAsync(seq, build(seq), timeout ?? _options.ApplyTimeout, cancellationToken);
    }

    /// <summary>Top-right of the monitor under the cursor, in a grid that fits its working area.</summary>
    private void Arrange(CardWindow[] cards)
    {
        if (cards.Length == 0)
        {
            return;
        }

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var scale = cards[0].DeviceDpi / 96.0;
        int S(int dip) => (int)Math.Round(dip * scale);
        int w = S(_options.CardWidthDip), h = S(_options.CardHeightDip), gap = S(_options.GapDip), m = S(_options.MarginDip);
        var cols = Math.Clamp((area.Width - 2 * m + gap) / (w + gap), 1, cards.Length);
        var width = Math.Min(w, area.Width - 2 * m);
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

        NoteFocus(cards);
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
