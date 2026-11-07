using System;
using System.Collections.Generic;
using System.Windows;

namespace UnboundKeys.Wpf;

public partial class SteeringPage : IDashboardPage
{
    private readonly Dictionary<string, MappingRow> _rows = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    // The three buttons at the top take the user to the page where the
    // Starts and stops steering switch lives (the shell shows the section).
    internal event Action<DashboardSection>? NavigateRequested;

    public string Title => "Steering";

    public SteeringPage()
    {
        InitializeComponent();

        // Push up and Push down: rows like the Mouse page's buttons but with
        // no place on the drawn mouse. Their editor is the plain one
        // (DashboardShell.OpenEditor).
        foreach (var push in MouseCatalog.Pushes)
        {
            string id = push.Id;
            var row = new MappingRow(push.Label, MappingRow.ValueOf(SteeringMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(SteeringMapSource.Instance, id), null);
            row.Clicked += () => EditRequested?.Invoke(SteeringMapSource.Instance, id, push.Label);
            _rows[id] = row;
            PushRows.Children.Add(row);
        }

        // Steering is for the session, the stretch is saved. Changed can
        // fire from the hook's thread or a panic path's, so the page is
        // redrawn through the dispatcher.
        GoMouseButton.Click += (_, _) => NavigateRequested?.Invoke(DashboardSection.Mouse);
        GoKeyboardButton.Click += (_, _) => NavigateRequested?.Invoke(DashboardSection.Keyboard);
        GoVoiceButton.Click += (_, _) => NavigateRequested?.Invoke(DashboardSection.Voice);
        // The default one-button setup, in the active profile: Middle Button
        // sends Throttle in Hold with Infinite and starts and stops steering,
        // so one press is race on and the next is race off. Then the Mouse
        // page, where the Middle Button row shows it.
        // Moving down brakes, as a one-second Hold, not Infinite (Fizzil):
        // only one infinite hold runs per source, and the pushes share the
        // mouse buttons' slot, so an infinite brake bumped the throttle off
        // on the pointer's first dip and left the middle button's hold out
        // of phase with steering (steering came on, the car did not go). A
        // timed hold never claims that slot.
        DefaultButton.Click += (_, _) =>
        {
            MouseMap.Rebind("middle", PadKeys.Throttle);
            MouseMap.SetBehavior("middle", new KeyBehavior { Hold = true, Infinite = true, Steering = true });
            SteeringMap.Rebind("pushdown", PadKeys.Brake);
            SteeringMap.SetBehavior("pushdown", new KeyBehavior { Hold = true, DurationSeconds = 1 });
            Refresh();
            NavigateRequested?.Invoke(DashboardSection.Mouse);
        };
        StretchMinusButton.Click += (_, _) => Steering.SetStretch(Math.Round(Steering.Stretch - 0.05, 2));
        StretchPlusButton.Click += (_, _) => Steering.SetStretch(Math.Round(Steering.Stretch + 0.05, 2));
        StretchResetButton.Click += (_, _) => Steering.SetStretch(Steering.DefaultStretch);
        Steering.Changed += () => Dispatcher.InvokeAsync(ShowSteering);

        // The page that downloads the driver, in the default browser.
        GetPadDriverButton.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VirtualPadDriver.DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("opening the ViGEmBus download page", ex);
            }
        };
        Refresh();
    }

    private void ShowSteering()
    {
        var switches = SteeringSwitches();
        string state = Steering.IsOn ? "Steering is on." : "Steering is off.";
        StateText.Text = switches.Count == 0
            ? state + " Nothing switches it yet."
            : state + " " + string.Join(", ", switches) + (switches.Count == 1 ? " switches it." : " switch it.");
        StretchText.Text = $"{Math.Round(Steering.Stretch * 100)} %";
    }

    // Every mapping in the active profile with Steering on, named the way
    // the user knows it: "press nine", "Middle Button", "Middle Button
    // double press", "A" (a Keyboard-page key).
    private static List<string> SteeringSwitches()
    {
        var names = new List<string>();
        foreach (var word in KeyMap.RemappableWords)
            if (KeyMap.Behaviors.TryGetValue(word, out var b) && b.Steering)
                names.Add($"\"press {word}\"");
        foreach (var id in MouseMap.ButtonIds)
            if (MouseMap.Behaviors.TryGetValue(id, out var b) && b.Steering)
            {
                var (baseId, gesture) = Gestures.Split(id);
                names.Add(MouseCatalog.LabelFor(baseId) + (gesture == Gesture.Double ? " double press" : ""));
            }
        foreach (var id in VirtualKeyMap.KeyIds)
            if (VirtualKeyMap.Behaviors.TryGetValue(id, out var b) && b.Steering)
            {
                var (baseId, gesture) = Gestures.Split(id);
                string label = baseId;
                foreach (var key in VirtualKeyCatalog.Keys)
                    if (key.Id == baseId)
                        label = key.Label;
                names.Add(label + " key" + (gesture == Gesture.Double ? " double press" : ""));
            }
        return names;
    }

    // Whether the virtual controller can be made, and if the last try
    // failed, why in plain words.
    private void ShowPadDriver()
    {
        bool installed = VirtualPadDriver.IsInstalled();
        PadDriverStatus.Text = installed
            ? "Virtual Xbox controller: ready."
            : "Virtual Xbox controller: its driver is not installed.";
        GetPadDriverButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        string? problem = Steering.PadProblem;
        PadProblemText.Text = problem ?? "";
        PadProblemText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Refresh()
    {
        foreach (var (id, row) in _rows)
        {
            row.SetChipText(MappingRow.ValueOf(SteeringMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(SteeringMapSource.Instance, id), null);
        }
        ShowSteering();
        ShowPadDriver();
    }
}
