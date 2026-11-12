using System.Diagnostics;
using System.Drawing.Drawing2D;
using HeyGanesh.App;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Tray;

/// <summary>
/// The settings window (DEC-082), shaped like a notch: it hangs from the top edge of the screen under the cursor,
/// flat on top and rounded below, dark, and drops open and closes on a critically damped spring. It says who is
/// teaching, shows each provider's state in plain words and does the one thing that state needs: use it, paste a
/// key, sign in, or get the app. Nothing here needs a terminal. All rules live in <see cref="ProviderStatusModel"/>.
/// </summary>
/// <remarks>
/// Like the key screen it takes focus (a key is typed into it), so the tray pauses Ctrl+Alt while it is open, and
/// it is excluded from screen capture so a half-typed key can never be photographed by the app's own capture.
/// The drop is an ease, not a spring, when Windows animations are off (reduced motion). What was typed exists as a
/// .NET string until the box is cleared, as for any text box; it goes only to <see cref="ProviderStatusModel.SaveKey"/>.
/// </remarks>
internal sealed class SettingsPill : Form
{
    // Every length is in 96-dpi units and scaled by the window's own dpi (the process is PerMonitorV2), so the fonts
    // (points) and the boxes around them grow together; a fixed pixel height clipped the text at 150 %.
    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
    private int W => S(420);
    private int M => S(22);
    private int RowH => S(58);
    private int KeyH => S(50);
    private int Radius => S(24);

    // Windows High Contrast (or "more contrast") swaps the palette for the system's own colours: the dark custom one
    // would otherwise override what the person chose so that they could read at all.
    private static readonly bool HighContrast = SystemInformation.HighContrast;
    private static readonly Color Ink = HighContrast ? SystemColors.Window : Color.FromArgb(11, 11, 12);
    private static readonly Color Text_ = HighContrast ? SystemColors.WindowText : Color.FromArgb(245, 245, 247);
    private static readonly Color Muted = HighContrast ? SystemColors.WindowText : Color.FromArgb(142, 142, 147);
    private static readonly Color Fill = HighContrast ? SystemColors.Control : Color.FromArgb(44, 44, 46);
    private static readonly Color Hover = HighContrast ? SystemColors.Control : Color.FromArgb(58, 58, 60);
    private static readonly Color Pressed = HighContrast ? SystemColors.ControlDark : Color.FromArgb(28, 28, 30);
    private static readonly Color Green = HighContrast ? SystemColors.Highlight : Color.FromArgb(48, 209, 88);
    private static readonly Color Amber = HighContrast ? SystemColors.WindowText : Color.FromArgb(255, 214, 10);
    private static readonly Color Grey = HighContrast ? SystemColors.GrayText : Color.FromArgb(99, 99, 102);
    private static readonly Color Accent = HighContrast ? SystemColors.Highlight : Color.FromArgb(10, 132, 255);

    private readonly ProviderStatusModel _model;
    private readonly Label _headline;
    private readonly Label _message;
    private readonly Panel _keyPanel;
    private readonly TextBox _keyBox;
    private readonly Button _keySave;
    private readonly Button _recheck;
    private readonly Button _done;
    private readonly List<(Label Dot, Label Name, Label Detail, Button Action)> _rows = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15 };
    private IReadOnlyList<ProviderRow> _state = Array.Empty<ProviderRow>();
    private bool _keyOpen;
    private double _height, _velocity, _target;

    // Text that changes fades in from the background colour (0.18 s) instead of snapping; instant with animations off.
    private double _fadeMessage = 1, _fadeHeadline = 1;
    private Color _messageTone = Muted;

    // The ring around a focused button shows for the keyboard only: a click also focuses a button, and a ring
    // after every click reads as noise.
    private bool _keyboardFocus;
    private bool _canSave;

    public SettingsPill(ProviderStatusModel model)
    {
        _model = model;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Ink;
        ForeColor = Text_;
        Font = new Font("Segoe UI", 9.5f);
        Text = "Hey Ganesh settings";
        KeyPreview = true;
        DoubleBuffered = true;
        Size = new Size(W, 1);

        Controls.Add(Make("TEACHING WITH", new Font("Segoe UI Semibold", 7.5f), Muted, M, S(20), S(200)));
        _headline = Make(string.Empty, new Font("Segoe UI Semibold", 16f), Text_, M, S(36), W - 2 * M);
        Controls.Add(_headline);

        for (var i = 0; i < 3; i++)
        {
            var dot = Make("●", new Font("Segoe UI", 11f), Grey, M, 0, S(22));
            var name = Make(string.Empty, new Font("Segoe UI Semibold", 10f), Text_, M + S(26), 0, W - 2 * M - S(26) - S(104));
            var detail = Make(string.Empty, new Font("Segoe UI", 8.5f), Muted, M + S(26), 0, W - 2 * M - S(26) - S(104));
            var action = Pill(string.Empty, S(96));
            var index = i;
            action.Click += (_, _) => OnAction(_state[index]);
            foreach (var click in new[] { dot, name, detail })
            {
                click.Click += (_, _) => OnChoose(_state[index]);
            }

            Controls.AddRange([dot, name, detail, action]);
            _rows.Add((dot, name, detail, action));
        }

        _keyBox = new TextBox
        {
            UseSystemPasswordChar = true, MaxLength = 512, BorderStyle = BorderStyle.FixedSingle, BackColor = Fill,
            ForeColor = Text_, Font = new Font("Segoe UI", 10f), Location = new Point(0, S(8)), Width = W - 2 * M - S(106),
            AccessibleName = "DeepSeek key",
        };
        _keySave = Pill("Save", S(96));
        _keySave.Location = new Point(W - 2 * M - S(96), S(6));
        _keySave.Click += (_, _) => OnSaveKey();
        _keyBox.TextChanged += (_, _) => CheckKey();
        _keyBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OnSaveKey();
            }
        };
        _keyPanel = new Panel { BackColor = Ink, Size = new Size(W - 2 * M, KeyH), Visible = false };
        _keyPanel.Controls.AddRange([_keyBox, _keySave]);
        Controls.Add(_keyPanel);

        _message = Make(string.Empty, new Font("Segoe UI", 8.5f), Muted, M, 0, W - 2 * M);
        _message.Height = _message.Font.Height * 2 + S(2);
        Controls.Add(_message);
        _recheck = Pill("Recheck", S(96));
        _done = Pill("Done", S(96));
        _recheck.Click += (_, _) => Reload("Checked again.");
        _done.Click += (_, _) => Close();
        Controls.AddRange([_recheck, _done]);

        // Tab goes top to bottom: each row's button, with the key box right after DeepSeek's, then Recheck and Done.
        _rows[0].Action.TabIndex = 0;
        _keyPanel.TabIndex = 1;
        _rows[1].Action.TabIndex = 2;
        _rows[2].Action.TabIndex = 3;
        _recheck.TabIndex = 4;
        _done.TabIndex = 5;

        _clock.Tick += (_, _) => Step();
        Reload(string.Empty);
        CheckKey();
        Place();
    }

    // The test hooks: the window's own words and buttons, so nothing has to be read off pixels.
    internal string Headline => _headline.Text;
    internal string Message => _message.Text;
    internal IReadOnlyList<string> ActionTexts => _rows.Select(r => r.Action.Text).ToList();
    internal IReadOnlyList<string> DetailTexts => _rows.Select(r => r.Detail.Text).ToList();
    internal TextBox KeyBox => _keyBox;
    internal Button KeySaveButton => _keySave;
    internal bool KeyPanelOpen => _keyPanel.Visible;
    internal double TargetHeight => _target;
    internal Color MessageColor => _message.ForeColor;
    internal bool CanSave => _canSave;

    /// <summary>The names a screen reader would read in Tab order, for the visible, focusable controls.</summary>
    internal IReadOnlyList<string> TabStops()
    {
        var stops = new List<string>();
        for (var c = GetNextControl(this, true); c is not null && stops.Count < 20; c = GetNextControl(c, true))
        {
            if (c.TabStop && c.Visible && c.Enabled)
            {
                stops.Add(string.IsNullOrEmpty(c.AccessibleName) ? c.Text : c.AccessibleName);
            }
        }

        return stops;
    }

    internal void Press(int row) => _rows[row].Action.PerformClick();
    internal string AllLabelText() => string.Join("\n", Controls.OfType<Label>().Select(l => l.Text));

    protected override bool ShowWithoutActivation => false;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The capture contract: this window can show a key being typed, so the app's own capture must not see it.
        CaptureExclusion.TryExcludeFromCapture(Handle, out _);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _clock.Start();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) is Keys.Tab or Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            _keyboardFocus = true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Top centre of the monitor under the cursor, flush with the edge: the notch's seat.</summary>
    private void Place()
    {
        var area = Screen.FromPoint(Cursor.Position).Bounds;
        Location = new Point(area.Left + (area.Width - W) / 2, area.Top);
    }

    private void Reload(string message)
    {
        _state = _model.Rows();
        SetHeadline(_model.Headline());
        for (var i = 0; i < _rows.Count; i++)
        {
            var (dot, name, detail, action) = _rows[i];
            var row = _state[i];
            name.Text = row.Name;
            detail.Text = row.Detail;
            dot.ForeColor = row.State switch { ProviderState.Ready => Green, ProviderState.NotSignedIn or ProviderState.NeedsKey => Amber, _ => Grey };
            action.Text = row.State switch
            {
                ProviderState.Ready => row.Active ? "Active" : "Use",
                ProviderState.NotSignedIn => "Sign in",
                ProviderState.NeedsKey => _keyOpen ? "Close" : "Add key",
                _ => "Get",
            };
            action.AccessibleName = $"{row.Name}: {action.Text}"; // "Use" alone says nothing to a screen reader
            // The active row is a green label, not a disabled button (a disabled button's grey text vanishes on dark).
            var isActive = row.State == ProviderState.Ready && row.Active;
            action.ForeColor = isActive ? Green : Text_;
            action.BackColor = isActive ? Ink : Fill;
            action.Cursor = isActive ? Cursors.Default : Cursors.Hand;
            action.FlatAppearance.MouseOverBackColor = isActive ? Ink : Hover;
            action.FlatAppearance.MouseDownBackColor = isActive ? Ink : Pressed;
        }

        SetMessage(message, Muted);
        LayOut();
    }

    /// <summary>Positions everything for the current state and sets the height the spring chases.</summary>
    private void LayOut()
    {
        var y = _headline.Bottom + S(16);
        for (var i = 0; i < _rows.Count; i++)
        {
            var (dot, name, detail, action) = _rows[i];
            dot.Top = y + S(4);
            name.Top = y + S(6);
            detail.Top = name.Bottom;
            action.Location = new Point(W - M - action.Width, y + (RowH - action.Height) / 2 - S(4));
            y += RowH;
            if (i == 0)
            {
                _keyPanel.Visible = _keyOpen;
                _keyPanel.Location = new Point(M, y - S(8));
                if (_keyOpen)
                {
                    y += KeyH;
                }
            }
        }

        _message.Top = y;
        y = _message.Bottom + S(6);
        _recheck.Location = new Point(M, y);
        _done.Location = new Point(W - M - _done.Width, y);
        Retarget(_done.Bottom + S(24));
    }

    /// <summary>
    /// A new height to move to. The current height and velocity are kept, so a click mid-drop changes course instead
    /// of restarting (an interruptible spring, never an animation that must finish).
    /// </summary>
    private void Retarget(double height)
    {
        _target = height;
        if (!SystemInformation.UIEffectsEnabled)
        {
            _height = height;
            _velocity = 0;
            Apply();
        }
    }

    /// <summary>Damping ratio 1 (no overshoot), response 0.35 s: x'' = -k(x - target) - 2 sqrt(k) x'.</summary>
    private void Step()
    {
        const double dt = 0.015;
        const double response = 0.35;
        var k = Math.Pow(2 * Math.PI / response, 2);
        var acceleration = -k * (_height - _target) - 2 * Math.Sqrt(k) * _velocity;
        _velocity += acceleration * dt;
        _height += _velocity * dt;
        if (Math.Abs(_height - _target) < 0.4 && Math.Abs(_velocity) < 4)
        {
            _height = _target;
            _velocity = 0;
        }

        var fade = 0.015 / 0.18;
        _fadeMessage = Math.Min(1, _fadeMessage + fade);
        _fadeHeadline = Math.Min(1, _fadeHeadline + fade);
        _message.ForeColor = Lerp(Ink, _messageTone, _fadeMessage);
        _headline.ForeColor = Lerp(Ink, Text_, _fadeHeadline);

        Apply();
    }

    private static Color Lerp(Color from, Color to, double t) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * t), (int)Math.Round(from.G + (to.G - from.G) * t), (int)Math.Round(from.B + (to.B - from.B) * t));

    private void SetHeadline(string text)
    {
        if (_headline.Text == text)
        {
            return;
        }

        _headline.Text = text;
        _fadeHeadline = SystemInformation.UIEffectsEnabled ? 0 : 1;
        _headline.ForeColor = Lerp(Ink, Text_, _fadeHeadline);
    }

    private void SetMessage(string text, Color tone)
    {
        if (_message.Text == text && _messageTone == tone)
        {
            return;
        }

        _message.Text = text;
        _messageTone = tone;
        _fadeMessage = SystemInformation.UIEffectsEnabled ? 0 : 1;
        _message.ForeColor = Lerp(Ink, tone, _fadeMessage);
    }

    /// <summary>
    /// Inline, while typing: a key with a space in it cannot be saved and says why; a short one can, with a nudge.
    /// The Save button looks dim when it will do nothing (it stays enabled: a disabled button's text vanishes on dark).
    /// </summary>
    private void CheckKey()
    {
        var (canSave, hint) = ProviderStatusModel.CheckKey(_keyBox.Text);
        _canSave = canSave;
        _keySave.ForeColor = canSave ? Text_ : Grey;
        SetMessage(hint ?? string.Empty, Amber);
    }

    private void Apply()
    {
        var h = Math.Max(1, (int)Math.Round(_height));
        if (h != Height)
        {
            Height = h;
        }

        var r = Math.Min(Radius, Math.Max(1, h / 2));
        using var path = new GraphicsPath();
        path.AddLine(0, 0, W, 0);
        path.AddLine(W, 0, W, h - r);
        path.AddArc(W - 2 * r, h - 2 * r, 2 * r, 2 * r, 0, 90);
        path.AddLine(W - r, h, r, h);
        path.AddArc(0, h - 2 * r, 2 * r, 2 * r, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }

    private void OnChoose(ProviderRow row)
    {
        if (row.State != ProviderState.Ready || row.Active)
        {
            return;
        }

        var message = _model.Choose(row.Id);
        Reload(message);
    }

    private void OnAction(ProviderRow row)
    {
        switch (row.State)
        {
            case ProviderState.Ready:
                OnChoose(row);
                break;
            case ProviderState.NeedsKey:
                _keyOpen = !_keyOpen;
                Reload(string.Empty);
                if (_keyOpen)
                {
                    _keyBox.Focus();
                }

                break;
            case ProviderState.NotSignedIn:
                Launch(ProviderStatusModel.SignIn(row.Id), "A sign-in window opened. Finish there, then press Recheck.");
                break;
            default:
                OpenPage(ProviderStatusModel.InstallPage(row.Id));
                break;
        }
    }

    private void OnSaveKey()
    {
        if (!_canSave)
        {
            return;
        }

        var message = _model.SaveKey(_keyBox.Text);
        _keyBox.Clear();
        _keyOpen = message.StartsWith("Not saved", StringComparison.Ordinal);
        Reload(message);
    }

    private void Launch((string File, string Arguments)? command, string message)
    {
        if (command is not { } c)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(c.File, c.Arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            SetMessage(message, Muted);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetMessage("Could not open the sign-in window.", Muted);
        }
    }

    private void OpenPage(string? url)
    {
        if (url is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            SetMessage("Opened the download page. Install it, then press Recheck.", Muted);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetMessage("Could not open the browser.", Muted);
        }
    }

    private Label Make(string text, Font font, Color color, int x, int y, int width) =>
        new() { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, Location = new Point(x, y), Width = width, Height = font.Height + S(4), AutoEllipsis = true };

    /// <summary>A flat, rounded-feeling button: instant pressed feedback (a darker fill on mouse down), no system chrome.</summary>
    private Button Pill(string text, int width)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = S(30), FlatStyle = FlatStyle.Flat, BackColor = Fill, ForeColor = Text_,
            Font = new Font("Segoe UI Semibold", 9f), UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.BorderSize = HighContrast ? 1 : 0;
        button.FlatAppearance.MouseOverBackColor = Hover;
        button.FlatAppearance.MouseDownBackColor = Pressed;
        button.MouseDown += (_, _) => _keyboardFocus = false;
        button.GotFocus += (_, _) => button.FlatAppearance.BorderSize = _keyboardFocus ? 2 : (HighContrast ? 1 : 0);
        button.LostFocus += (_, _) => button.FlatAppearance.BorderSize = HighContrast ? 1 : 0;
        return button;
    }
}
