using System;
using System.Windows;

namespace UnboundKeys.Wpf;

public partial class DashboardWindow
{
    public event Action? QuitRequested;
    // The X was clicked: the window hid to the tray (Program tells the tray
    // icon, which says so once).
    public event Action? HiddenByClose;

    private readonly DashboardShell _shell;

    // At startup: the on-screen keyboard comes back if it was open when the
    // app was last quit (see DashboardShell.RestoreKeyboard).
    public bool RestoreKeyboard() => _shell.RestoreKeyboard();

    public DashboardWindow()
    {
        InitializeComponent();

        // Built here in code rather than in the XAML because
        // DashboardShell's constructor is internal.
        var shell = new DashboardShell();
        _shell = shell;
        shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
        shell.CloseRequested += () =>
        {
            Hide();
            HiddenByClose?.Invoke();
        };
        shell.QuitRequested += () => QuitRequested?.Invoke();
        shell.ToggleRequested += ToggleVisible;
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

        // A second copy of the app, started by mistake, asks this one to
        // show itself before it quits (see SingleInstance).
        SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg == SingleInstance.ShowDashboardMessage)
                {
                    ShowDashboard();
                    handled = true;
                }
                return IntPtr.Zero;
            });

        // The window gets its handle now rather than at its first Show. The
        // app can start with only the on-screen keyboard up (see
        // RestoreKeyboard) and this window never shown; without a handle
        // there was nothing to receive the request above, so a second click
        // on the shortcut did nothing at all.
        new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
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
