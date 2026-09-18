namespace VoicePress;

// A tab hosted in the dashboard's action bar (or, like Profile, pinned to
// its own slot to the left of it) — the shell (DashboardForm) only ever
// talks to a tab through this, so it doesn't need to know what a "word
// card" or "Profiles" actually are. Adding a new tab means writing a class
// that implements this and handing it to the shell — nothing else changes.
internal interface IDashboardTab
{
    string Id { get; }
    string Label { get; }
    Control BuildContent(DashboardTabContext ctx);
}

// What a tab gets handed to talk back to the shell, instead of reaching
// into DashboardForm's private fields directly.
internal sealed class DashboardTabContext
{
    // The card row height every list-style tab uses (Key/Repeat/Hold/Reset
    // rows, Profiles' rows) — the same fixed value the shell already sizes
    // everything else around.
    public required int ItemHeight { get; init; }

    // Tells the shell this tab's required height changed (e.g. a row was
    // added or removed) — the shell resizes the window if this tab happens
    // to be the one currently selected.
    public required Action<int> ReportHeight { get; init; }

    // Clears the dashboard window's own focus, so a clicked button's focus
    // rectangle doesn't linger (matches the ActiveControl = null pattern
    // used elsewhere after rebuilding a list).
    public required Action ClearFocus { get; init; }

    // Switches the active profile — implemented by the shell, since doing
    // so also means rebuilding the ten word cards against the new
    // profile's data, which isn't something the Profiles tab owns.
    public required Action<string> SwitchToProfile { get; init; }

    // Registers a callback the shell invokes after any profile switch, so
    // this tab can refresh which row is highlighted as "active" — needed
    // because a switch can also be triggered from outside this tab.
    public required Action<Action> OnProfileSwitched { get; init; }

    // The fixed height of an accordion row (the +1/+0.1/reset/Infinite
    // timing strip) and of a whole card with nothing expanded —
    // RemapCardTab needs both to compute its own required height.
    public required int AccordionHeight { get; init; }
    public required int BaseCardHeight { get; init; }

    // Opens the floating list of one key category's individual keys,
    // closing whatever was already open first — only one should ever be
    // showing at a time, across every word card.
    public required Action<Control, KeyCatalog.Entry[], Action<KeyCatalog.Entry>> ShowCategoryPopup { get; init; }

    // Closes the category popup if one happens to be open, otherwise a
    // no-op — used when collapsing/resetting a card out from under it.
    public required Action CloseCategoryPopup { get; init; }

    // Registers this card's own "reset everything about this card" action
    // into the shell's shared registry, so the "Reset All" easter egg (on
    // any card) can run every card's reset at once, including ones that
    // aren't currently visible.
    public required Action<Action> RegisterResetAction { get; init; }

    // Runs every registered card's reset action at once (see
    // RegisterResetAction) — triggered by the "Reset All" row.
    public required Action ResetAllCards { get; init; }

    // Freezes/thaws the dashboard window's own screen updates — wrap a
    // multi-step change (rebuilding a whole list of rows, say) between
    // these so nothing partially-updated flashes on screen partway through.
    // Safely nestable: only the outermost Begin/End pair actually does
    // anything, so it's fine to call these even from within a callback
    // (like ReportHeight) that might itself already be wrapped by the shell.
    public required Action BeginScreenUpdate { get; init; }
    public required Action EndScreenUpdate { get; init; }
}
