using UnboundKeys.Wpf;

namespace UnboundKeys.Tests;

// Where Back goes in the dashboard (Fizzil, 2026-11-11): out of a mapping's
// editor to its page, and out of Help to wherever Help was opened from,
// page or editor. ‹ Back, a right-click and (on Help) the ? clicked again
// all go the same way. DashboardBack decides; DashboardShell moves.
internal static class DashboardBackTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("dashboard: where Back goes", Back);
        return _ok;
    }

    private static void Back()
    {
        var back = new DashboardBack();
        Check(back.Next(editorOpen: false, DashboardSection.Voice) == DashboardBack.Step.Nothing,
            "an ordinary page has nowhere to go back to");
        Check(back.Next(editorOpen: true, DashboardSection.Voice) == DashboardBack.Step.CloseEditor,
            "an editor goes back to its page");

        var voice = new DashboardPlace(DashboardSection.Voice);
        back.HelpOpened(voice);
        back.Shown(DashboardSection.Help);
        Check(back.Next(editorOpen: false, DashboardSection.Help) == DashboardBack.Step.LeaveHelp,
            "Help goes back");
        Check(back.BeforeHelp == voice, "to the page it was opened from");

        var editor = new DashboardPlace(DashboardSection.Voice, null, "one", "press one");
        back.HelpOpened(editor);
        back.Shown(DashboardSection.Help);
        Check(back.BeforeHelp == editor, "or to the editor it was opened from");

        back.Shown(DashboardSection.Help);
        Check(back.BeforeHelp == editor, "Help redrawn (a profile switch) keeps its way back");

        back.Shown(DashboardSection.Mouse);
        Check(back.BeforeHelp == null, "another page shown forgets it");
        Check(back.Next(editorOpen: false, DashboardSection.Help) == DashboardBack.Step.Nothing,
            "so Help reached with nowhere remembered has no Back");
    }

    private static void Section(string name, Action body)
    {
        Console.WriteLine($"--- {name} ---");
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}
