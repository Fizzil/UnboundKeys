namespace UnboundKeys;

// The remap card (Key/Add Key/Repeat/Hold/Infinite/Reset — the same
// RemapCardTab UI Mouse already uses) for one virtual-keyboard key,
// opened by right-clicking it on VirtualKeyboardForm. A separate
// floating popup rather than embedded in the dashboard, since the keyboard
// is its own window with no drawer of its own to hold this — same
// NonActivatingForm-based shape as ThemeColorPopup/CategoryKeyPopup, just
// hosting a full remap card instead of a short list of swatches/keys.
internal static class VirtualKeyRemapPopup
{
    // RemapCardTab was designed against DashboardForm's own drawer/card
    // sizing constants — reused unchanged here (as plain local copies, same
    // as DashboardForm's own WM_SETREDRAW pair below) so the card looks and
    // behaves identically to Mouse's, just in a standalone window.
    private const int CardWidth = 370;
    private const int ItemHeight = 81;
    private const int AccordionHeight = 56;
    private const int BaseCardHeight = 324;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETREDRAW = 0x000B;

    public static Form Show(Form owner, Control anchor, string keyId, string label)
    {
        Form? openCategoryPopup = null;
        Control? openCategoryAnchor = null;
        Action? resetAction = null;
        int screenUpdateDepth = 0;

        var popup = new NonActivatingForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Theme.Current.Background,
            ClientSize = new Size(CardWidth, BaseCardHeight),
        };

        void BeginScreenUpdate()
        {
            if (screenUpdateDepth == 0)
                SendMessage(popup.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            screenUpdateDepth++;
        }

        void EndScreenUpdate()
        {
            screenUpdateDepth--;
            if (screenUpdateDepth == 0)
            {
                SendMessage(popup.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                popup.Invalidate(true);
                popup.Update();
            }
        }

        var ctx = new DashboardTabContext
        {
            ItemHeight = ItemHeight,
            ReportHeight = height =>
            {
                BeginScreenUpdate();
                popup.ClientSize = new Size(CardWidth, height);
                Reposition(popup, anchor);
                EndScreenUpdate();
            },
            ClearFocus = () => popup.ActiveControl = null,
            // Only one card ever lives in this popup, and it's never
            // Profiles — SwitchToProfile/OnProfileSwitched are unused by
            // RemapCardTab (only ProfilesTab calls them), so both are safe
            // no-ops here.
            SwitchToProfile = _ => { },
            OnProfileSwitched = _ => { },
            AccordionHeight = AccordionHeight,
            BaseCardHeight = BaseCardHeight,
            ShowCategoryPopup = (categoryAnchor, keys, onSelect) =>
            {
                openCategoryPopup?.Close();
                openCategoryPopup = CategoryKeyPopup.Show(popup, categoryAnchor, CardWidth, keys, onSelect);
                openCategoryAnchor = categoryAnchor;
            },
            CloseCategoryPopup = () =>
            {
                openCategoryPopup?.Close();
                openCategoryPopup = null;
            },
            // Only one card is ever open in this popup, so "reset every
            // registered card" (DashboardForm's own Reset All) collapses
            // down to just this one stored action instead of a dictionary.
            RegisterResetAction = action => resetAction = action,
            ResetAllCards = () => resetAction?.Invoke(),
            BeginScreenUpdate = BeginScreenUpdate,
            EndScreenUpdate = EndScreenUpdate,
        };

        var card = new RemapCardTab(VirtualKeyMapSource.Instance, keyId, label).BuildContent(ctx);
        popup.Controls.Add(card);

        // Keeps an open category popup glued to this card if the keyboard
        // window itself is dragged while it's showing — same idea as
        // DashboardForm's own RepositionCategoryPopup.
        popup.LocationChanged += (_, _) =>
        {
            if (openCategoryPopup != null && !openCategoryPopup.IsDisposed && openCategoryAnchor != null)
                CategoryKeyPopup.Reposition(openCategoryPopup, openCategoryAnchor);
        };

        Reposition(popup, anchor);
        popup.Show(owner);
        popup.ActiveControl = null;
        return popup;
    }

    // Directly below the clicked key, clamped to stay fully on-screen —
    // the keyboard can be dragged near any edge, unlike the dashboard's own
    // popups which only ever open from a fixed position.
    public static void Reposition(Form popup, Control anchor)
    {
        var anchorScreenPoint = anchor.PointToScreen(Point.Empty);
        var screen = Screen.PrimaryScreen!.WorkingArea;

        int x = Math.Min(anchorScreenPoint.X, screen.Right - popup.Width);
        x = Math.Max(x, screen.Left);

        int y = anchorScreenPoint.Y + anchor.Height;
        if (y + popup.Height > screen.Bottom)
            y = anchorScreenPoint.Y - popup.Height; // no room below — flip above the key instead

        popup.Location = new Point(x, y);
    }
}
