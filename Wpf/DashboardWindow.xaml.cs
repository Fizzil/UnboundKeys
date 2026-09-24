using System;
using System.Windows;

namespace UnboundKeys.Wpf;

public partial class DashboardWindow
{
    public event Action? QuitRequested;

    public DashboardWindow()
    {
        InitializeComponent();

        // Built here in code rather than in the XAML because
        // DashboardShell's constructor is internal.
        var shell = new DashboardShell();
        shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
        shell.CloseRequested += Hide;
        shell.QuitRequested += () => QuitRequested?.Invoke();
        shell.ShowRequested += ShowDashboard;
        Root.Children.Add(shell);

        var (left, top) = Settings.LoadDashboardPlacement();
        if (left is double savedLeft && top is double savedTop)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedLeft;
            Top = savedTop;
        }

        // Fade can be lifted from the keyboard hook's thread (the physical
        // Caps Lock panic tap), so the handler hops to this window's thread.
        FadeMode.Changed += () => Dispatcher.InvokeAsync(ApplyFade);
        ApplyFade();

        Loaded += (_, _) => KeepOnScreen();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
                SavePlacement();
        };
        Closing += (_, _) => SavePlacement();
    }

    public void ToggleVisible()
    {
        if (IsVisible)
            Hide();
        else
            ShowDashboard();
    }

    // Brings the dashboard back wherever it was last left (restored if it
    // had been minimized), kept on screen in case that spot is gone.
    public void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        KeepOnScreen();
    }

    private void ApplyFade() => Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;

    private void SavePlacement()
    {
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
            Settings.SaveDashboardPlacement(Left, Top);
    }
}
