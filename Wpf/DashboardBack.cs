namespace UnboundKeys.Wpf;

// Where you were in the dashboard: a page, or a mapping's editor on it
// (the source, id and title OpenEditor was given).
internal sealed record DashboardPlace(DashboardSection Section, IRemapSource? Source = null, string? Id = null, string? Title = null);

// Where Back goes (Fizzil, 2026-11-11): out of a mapping's editor to its
// page, and out of Help to wherever Help was opened from, page or editor.
// ‹ Back, a right-click and, on Help, the ? clicked again all ask this.
// It only decides; DashboardShell moves. The way back out of Help is
// remembered while Help shows (redrawn by a profile switch, too) and
// forgotten as soon as any other page is shown.
internal sealed class DashboardBack
{
    public enum Step { Nothing, CloseEditor, LeaveHelp }

    public DashboardPlace? BeforeHelp { get; private set; }

    public void HelpOpened(DashboardPlace from) => BeforeHelp = from;

    public void Shown(DashboardSection section)
    {
        if (section != DashboardSection.Help)
            BeforeHelp = null;
    }

    public Step Next(bool editorOpen, DashboardSection current) =>
        editorOpen ? Step.CloseEditor
        : current == DashboardSection.Help && BeforeHelp != null ? Step.LeaveHelp
        : Step.Nothing;
}
