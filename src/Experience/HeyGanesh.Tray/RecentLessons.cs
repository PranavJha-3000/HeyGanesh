namespace HeyGanesh.Tray;

/// <summary>
/// The Recent lessons window (DEC-060): past turns newest first, the selected one in words, and Replay, Delete and
/// Clear all. The rules live in <see cref="RecentLessonsModel"/>; this class lays them out. Replay is handed in by the
/// tray (the shell's <c>ReplayAsync</c>), so the window never plans or calls a model itself.
/// </summary>
internal sealed class RecentLessons : Form
{
    private readonly RecentLessonsModel _model;
    private readonly Func<string, Task<string>> _replay;
    private readonly ListView _list;
    private readonly TextBox _detail;
    private readonly Button _replayButton;
    private readonly Button _delete;
    private readonly Button _clear;
    private readonly Label _status;
    private volatile bool _replaying;

    /// <param name="replay">Plays a stored plan and returns a sentence about how it went. Never throws for a bad plan.</param>
    public RecentLessons(RecentLessonsModel model, Func<string, Task<string>> replay)
    {
        _model = model;
        _replay = replay;
        Text = "Hey Ganesh: recent lessons";
        Icon = TrayIcons.Load(SystemInformation.IconSize);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(860, 560);
        MinimumSize = new Size(620, 400);

        _list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        _list.Columns.Add("When", 140);
        _list.Columns.Add("Question", 330);
        _list.Columns.Add("Taught", 330);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();

        _detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        // Set once the window has its size: a splitter distance beyond the default small panel throws.
        Load += (_, _) => split.SplitterDistance = Math.Max(120, split.Height * 55 / 100);
        split.Panel1.Controls.Add(_list);
        split.Panel2.Controls.Add(_detail);

        _replayButton = new Button { Text = "Replay", AutoSize = true, Enabled = false };
        _delete = new Button { Text = "Delete", AutoSize = true, Enabled = false };
        _clear = new Button { Text = "Clear all…", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true };
        _status = new Label { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };
        _replayButton.Click += async (_, _) => await ReplaySelectedAsync();
        _delete.Click += (_, _) => DeleteSelected();
        _clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Delete every saved turn? This cannot be undone.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                ClearAll();
            }
        };
        close.Click += (_, _) => Close();
        CancelButton = close;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange(new Control[] { _replayButton, _delete, _clear, close, _status });
        Controls.Add(split);
        Controls.Add(buttons);
        Reload();
    }

    /// <summary>True while a replay plays: the tray pauses Ctrl+Alt meanwhile.</summary>
    public bool Replaying => _replaying;

    internal IReadOnlyList<string> RowTexts => _list.Items.Cast<ListViewItem>().Select(i => string.Join(" | ", i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text))).ToList();

    internal string DetailText => _detail.Text;

    internal string StatusText => _status.Text;

    internal bool ReplayEnabled => _replayButton.Enabled;

    internal void Select(int index)
    {
        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(index);
    }

    internal Task ReplaySelectedAsync() => SelectedRow is { CanReplay: true } row ? ReplayAsync(row) : Task.CompletedTask;

    internal void DeleteSelected()
    {
        if (SelectedRow is { } row && _model.Delete(row.Id))
        {
            Reload($"Deleted the turn \"{row.Question}\".");
        }
    }

    internal void ClearAll() => Reload($"Cleared {_model.Clear()} saved turn(s).");

    private LessonRow? SelectedRow => _list.SelectedItems.Count == 1 ? (LessonRow)_list.SelectedItems[0].Tag! : null;

    private async Task ReplayAsync(LessonRow row)
    {
        if (_replaying || _model.PlanJson(row.Id) is not { } plan)
        {
            return;
        }

        _replaying = true;
        UpdateButtons();
        _status.Text = $"Replaying \"{row.Question}\"… (Ctrl+Alt is paused until it ends)";
        try
        {
            _status.Text = await _replay(plan);
        }
        catch (Exception ex)
        {
            _status.Text = $"The replay stopped: {ex.GetType().Name}.";
        }
        finally
        {
            _replaying = false;
            UpdateButtons();
        }
    }

    private void Reload(string? status = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _model.Rows())
        {
            _list.Items.Add(new ListViewItem(new[] { row.When, row.Question, row.Taught }) { Tag = row });
        }

        _list.EndUpdate();
        _detail.Text = string.Empty;
        _status.Text = status ?? (_list.Items.Count == 0 ? "No saved turns yet. Ask with Ctrl+Alt, and each taught turn is kept here." : $"{_list.Items.Count} saved turn(s).");
        UpdateButtons();
    }

    private void ShowSelected()
    {
        _detail.Text = SelectedRow is { } row ? _model.Detail(row.Id).ReplaceLineEndings("\r\n") : string.Empty;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _replayButton.Enabled = !_replaying && SelectedRow is { CanReplay: true };
        _delete.Enabled = !_replaying && SelectedRow is not null;
        _clear.Enabled = !_replaying && _list.Items.Count > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Dispose();
        }

        base.Dispose(disposing);
    }
}
