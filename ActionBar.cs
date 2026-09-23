namespace UnboundKeys;

// The horizontal strip of tab buttons across the top of the dashboard.
// Each tab gets its own fixed (never-changing-once-set) pixel width; new
// tabs are inserted at the left end, so existing tabs never get resized or
// reordered to make room for one more. Adding a new kind of tab to the
// dashboard means handing its button to Add() here; nothing about the bar
// itself needs to change.
internal sealed class ActionBar
{
    private readonly TableLayoutPanel _panel;
    private readonly List<(string Id, Button Button, int Width)> _tabs = new();
    private readonly int _defaultWidth;

    public Control Control => _panel;

    public ActionBar(int defaultWidth)
    {
        _defaultWidth = defaultWidth;
        _panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 0,
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
    }

    public void Add(string id, Button tabButton, int? width = null)
    {
        int tabWidth = width ?? _defaultWidth;
        _tabs.Insert(0, (id, tabButton, tabWidth));
        Rebuild();
    }

    // Re-lays-out the tabs in their current left-to-right order, each at
    // its own fixed width.
    private void Rebuild()
    {
        _panel.SuspendLayout();
        _panel.Controls.Clear();
        _panel.ColumnStyles.Clear();
        _panel.ColumnCount = _tabs.Count;

        for (int i = 0; i < _tabs.Count; i++)
        {
            var (_, tabButton, tabWidth) = _tabs[i];
            _panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, tabWidth));
            _panel.Controls.Add(tabButton, i, 0);
        }
        _panel.ResumeLayout(true);
    }
}
