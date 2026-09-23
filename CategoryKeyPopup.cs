namespace UnboundKeys;

// The floating list of one key category's individual keys (e.g. every
// letter, under "Letters") — opened by hovering a category button in a
// word card's Key accordion. A plain borderless, non-activating window
// rather than a menu, so hovering across categories keeps working (a
// native ContextMenuStrip puts Windows into a "menu tracking" mode that
// blocks MouseEnter on sibling controls while it's open) and so it never
// steals window activation away from the dashboard.
internal static class CategoryKeyPopup
{
    private const int RowHeight = 34;

    // No scrollbar even for the longest category (Letters, 26 keys) — this
    // just sizes the popup tall enough to show every key at once, since a
    // themed scrollbar isn't worth building for one edge case and the
    // built-in one clashes badly with the black/red look.
    public static Form Show(Form owner, Control anchor, int width, KeyCatalog.Entry[] keys, Action<KeyCatalog.Entry> onKeySelected)
    {
        var popup = new NonActivatingForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Theme.Current.Button,
            ClientSize = new Size(width, keys.Length * RowHeight),
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = keys.Length,
            BackColor = Theme.Current.Button,
        };
        for (int i = 0; i < keys.Length; i++)
        {
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

            var entry = keys[i];
            var item = Theme.MakeListButton(entry.DisplayName);
            item.Font = new Font("Segoe UI", 10f);
            item.Click += (_, _) =>
            {
                onKeySelected(entry);
                popup.Close();
            };
            list.Controls.Add(item, 0, i);
        }

        popup.Controls.Add(list);

        Reposition(popup, anchor);
        popup.Show(owner);
        popup.ActiveControl = null; // otherwise the first key shows a focus outline immediately
        return popup;
    }

    // Positions a category popup relative to whichever category button
    // opened it. Called both for initial placement and to keep it glued to
    // its card while the dashboard itself moves (e.g. while the listener
    // icon is being dragged) — see DashboardForm's RepositionCategoryPopup.
    //
    // To the left of the anchor, not overlapping it: sitting at the same X
    // as the anchor (tried briefly) rendered the individual keys directly
    // over the category list itself instead of as a distinct flyout beside
    // it, which read as more confusing than the popup's width ever did.
    public static void Reposition(Form popup, Control anchor)
    {
        var anchorScreenPoint = anchor.PointToScreen(Point.Empty);
        popup.Location = new Point(anchorScreenPoint.X - popup.Width, anchorScreenPoint.Y);
    }
}
