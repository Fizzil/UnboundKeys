namespace UnboundKeys.Wpf;

// What DashboardShell needs from a list page (Mouse, Voice, Keyboard,
// Settings): a title for the header, and a way to re-read its rows after
// something changed underneath it — coming back from an editor, a
// profile switch, Reset All.
internal interface IDashboardPage
{
    string Title { get; }
    void Refresh();
}
