namespace UnboundKeys;

// The color picker opened by four rapid right-clicks on the overlay icon
// (see OverlayForm). Only ever shows the colors that AREN'T currently
// active — with only three colors total, that's always exactly two — Red
// first if it's one of them, otherwise Green then Blue (matching the
// source image's own left-to-right order). Each row is the real skull
// artwork for that color (via OverlayForm.BuildSkullImage), sized exactly
// like the overlay icon itself and stacked flush beneath it, so opening
// the picker reads as "the active skull on top, the alternatives stacked
// directly under it" rather than a separate, disconnected menu.
//
// Clicking a swatch previews that color on the fly — including live on
// the dashboard if one's open, see OverlayForm.OnThemeChanged — without
// closing the picker, so you can click back and forth to compare. The
// row list rebuilds itself right after (the just-picked color drops out,
// whichever was active before takes its place), so the two rows always
// show "whatever's not active right now." There's no swatch for the
// active color itself to click closed — the picker dismisses the same
// way every other one does, by clicking the overlay icon again (see
// OverlayForm's own MouseUp handler).
internal static class ThemeColorPopup
{
    private static readonly string[] AllNames = { "Red", "Green", "Blue" };

    public static Form Show(Form owner, Control anchor, Action<string> onColorSelected)
    {
        int size = OverlayForm.TargetWidth;

        var popup = new NonActivatingForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Theme.Current.Background,
            // Always exactly two rows — always two inactive colors,
            // whichever they currently are — so the picker's own size
            // never needs to change, just which names fill its two rows.
            ClientSize = new Size(size, 2 * size),
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Current.Background,
        };
        // Explicit, not left to whatever a single unstyled column defaults
        // to — without this the column's actual width was less certain
        // than the row heights below it, and a column even slightly wider
        // than `size` would stretch every swatch exactly like the ones
        // being reported as distorted.
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, size));
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, size));
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, size));
        popup.Controls.Add(list);

        void RebuildRows()
        {
            list.Controls.Clear();
            var names = InactiveNamesInOrder();
            for (int i = 0; i < names.Count; i++)
            {
                var name = names[i];
                var swatch = new PictureBox
                {
                    Image = OverlayForm.BuildSkullImage(name),
                    SizeMode = PictureBoxSizeMode.StretchImage,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0),
                    Cursor = Cursors.Hand,
                };
                swatch.Click += (_, _) =>
                {
                    onColorSelected(name);
                    RebuildRows();
                };
                list.Controls.Add(swatch, 0, i);
            }
        }
        RebuildRows();

        Reposition(popup, anchor);
        popup.Show(owner);
        popup.ActiveControl = null; // otherwise the first key shows a focus outline immediately
        return popup;
    }

    // Directly below the anchor (the overlay icon), flush against its left
    // edge — matching widths exactly is what makes this read as a stack
    // rather than a floating menu. Called both for initial placement and
    // to keep it glued to the icon while it's being dragged — see
    // OverlayForm's own RepositionColorPopup.
    public static void Reposition(Form popup, Control anchor)
    {
        var anchorScreenPoint = anchor.PointToScreen(Point.Empty);
        popup.Location = new Point(anchorScreenPoint.X, anchorScreenPoint.Y + anchor.Height);
    }

    private static List<string> InactiveNamesInOrder()
    {
        var inactive = new List<string>();
        foreach (var name in AllNames)
            if (name != ThemeMode.Current)
                inactive.Add(name);

        // Red first if it's one of the two inactive colors — otherwise
        // the loop above already left Green before Blue.
        if (inactive.Remove("Red"))
            inactive.Insert(0, "Red");

        return inactive;
    }
}
