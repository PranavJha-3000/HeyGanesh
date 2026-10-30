namespace HeyGanesh.Tray;

/// <summary>
/// The key screen (DEC-053): one masked box per key, with Save and Remove. Nothing is written until Save is
/// clicked, the box is cleared straight after, and the screen shows a key only as "missing" or
/// "present (N chars, source)". All rules live in <see cref="KeyScreenModel"/>; this class only lays them out.
/// </summary>
/// <remarks>
/// This is the tray's one window that takes focus, on purpose: it is for typing. While it is open the tray
/// tells the shell to refuse Ctrl+Alt (see TrayApp). What was typed exists as a .NET string until the box is
/// cleared, the same as for any text box; it is handed only to <see cref="Security.KeyRing.Store"/>.
/// </remarks>
internal sealed class KeyScreen : Form
{
    private readonly KeyScreenModel _model;
    private readonly Dictionary<string, (Label Status, TextBox Box, Button Save, Button Remove)> _rows = new(StringComparer.Ordinal);
    private readonly Label _message;

    public KeyScreen(KeyScreenModel model)
    {
        _model = model;
        Text = "Hey Ganesh keys";
        Icon = TrayIcons.Load(SystemInformation.IconSize);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = "Keys are stored in Windows Credential Manager for your account only. This screen never shows a key, only whether one is there.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 3);

        var row = 1;
        foreach (var key in _model.Keys)
        {
            var name = key.Name;
            layout.Controls.Add(new Label { Text = KeyScreenModel.Purpose(name), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 0) }, 0, row);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, row)!, 3);
            row++;

            var status = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
            layout.Controls.Add(status, 0, row);
            layout.SetColumnSpan(status, 3);
            row++;

            var box = new TextBox { UseSystemPasswordChar = true, Width = 340, MaxLength = 512, AccessibleName = $"{name} key" };
            var save = new Button { Text = "Save", AutoSize = true };
            var remove = new Button { Text = "Remove", AutoSize = true };
            save.Click += (_, _) => OnSave(name);
            remove.Click += (_, _) => OnRemove(name);
            layout.Controls.Add(box, 0, row);
            layout.Controls.Add(save, 1, row);
            layout.Controls.Add(remove, 2, row);
            row++;

            _rows[name] = (status, box, save, remove);
        }

        _message = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 12, 0, 8) };
        layout.Controls.Add(_message, 0, row);
        layout.SetColumnSpan(_message, 3);
        row++;

        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        close.Click += (_, _) => Close();
        layout.Controls.Add(close, 2, row);
        CancelButton = close;

        Controls.Add(layout);
        UpdateState(string.Empty);
    }

    internal TextBox Box(string name) => _rows[name].Box;

    internal Button SaveButton(string name) => _rows[name].Save;

    internal Button RemoveButton(string name) => _rows[name].Remove;

    internal string StatusText(string name) => _rows[name].Status.Text;

    internal string Message => _message.Text;

    /// <summary>Every label's text on the screen, for TK-2's "no value anywhere" check.</summary>
    internal string AllLabelText() => string.Join("\n", Descendants(this).OfType<Label>().Select(l => l.Text));

    private void OnSave(string name)
    {
        var box = _rows[name].Box;
        var message = _model.Save(name, box.Text);
        box.Clear();
        UpdateState(message);
    }

    private void OnRemove(string name) => UpdateState(_model.Remove(name));

    private void UpdateState(string message)
    {
        foreach (var (name, row) in _rows)
        {
            row.Status.Text = $"Status: {_model.Status(name)}";
        }

        _message.Text = message;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }
}
