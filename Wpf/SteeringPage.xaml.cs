using System;
using System.Collections.Generic;
using System.Windows;

namespace UnboundKeys.Wpf;

public partial class SteeringPage : IDashboardPage
{
    private readonly Dictionary<string, MappingRow> _rows = new();
    private readonly MappingRow _raceButtonRow;
    private readonly System.Windows.Controls.Grid _racePicker;
    private bool _racePickerOpen;

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Steering";

    public SteeringPage()
    {
        InitializeComponent();

        // The race button: a row whose chip names the mouse button or key,
        // with the key picker the editor uses opening under it, and a row
        // for the race mapping itself (Throttle, Hold, Infinite, Steering
        // to start with), edited in the plain editor like a push.
        _raceButtonRow = new MappingRow("Race button", SteeringMap.TriggerLabel());
        _raceButtonRow.Clicked += ToggleRacePicker;
        RaceRows.Children.Add(_raceButtonRow);

        // Fixed height, collapsed until the row is clicked (as in the
        // editor's key rows); filled afresh on each opening so it marks the
        // current choice.
        _racePicker = new System.Windows.Controls.Grid { Visibility = Visibility.Collapsed, ClipToBounds = true, Height = 0 };
        _racePicker.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _racePicker.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RaceRows.Children.Add(_racePicker);

        var raceRow = new MappingRow("Race", MappingRow.ValueOf(SteeringMapSource.Instance, SteeringMap.RaceId));
        raceRow.SetModeGlyphs(MappingRow.BehaviorOf(SteeringMapSource.Instance, SteeringMap.RaceId), null);
        raceRow.Clicked += () => EditRequested?.Invoke(SteeringMapSource.Instance, SteeringMap.RaceId, "Race");
        _rows[SteeringMap.RaceId] = raceRow;
        RaceRows.Children.Add(raceRow);

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

    private const double RacePickerHeight = 320;

    private void ToggleRacePicker()
    {
        _racePickerOpen = !_racePickerOpen;
        if (_racePickerOpen)
        {
            _racePicker.Children.Clear();
            RemapCard.PopulateCategoryList(_racePicker, ChooseRaceTrigger, KeyCatalog.DisplayNameFor(SteeringMap.TriggerVk()));
        }
        RemapCard.AnimateHeight(_racePicker, _racePickerOpen, _racePickerOpen ? RacePickerHeight : 0,
            onExpanded: () => _racePicker.BringIntoView());
    }

    // What the picker chose becomes the race button if it is a mouse button
    // or a letter or digit key; anything else is refused in a line under
    // the rows and the picker stays open for another try.
    private void ChooseRaceTrigger(KeyCatalog.Entry entry)
    {
        string? id = SteeringMap.TriggerForVk(entry.VkCode);
        if (id == null)
        {
            RaceMessage.Text = "Pick a mouse button or a letter or digit";
            RaceMessage.Visibility = Visibility.Visible;
            return;
        }
        RaceMessage.Visibility = Visibility.Collapsed;
        SteeringMap.RaceTrigger = id;
        _racePickerOpen = false;
        RemapCard.AnimateHeight(_racePicker, false, 0);
        Refresh();
    }

    private void ShowSteering()
    {
        StateText.Text = Steering.IsOn ? "Steering is on." : "Steering is off.";
        var switches = SteeringSwitches();
        SwitchesText.Text = switches.Count == 0
            ? "Nothing switches it yet: give a word, mouse button or keyboard key the Steering switch in its editor."
            : (switches.Count == 1 ? switches[0] + " switches it on and off." : string.Join(", ", switches) + " switch it on and off.");
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
        PadDriverStatus.Text = VirtualPadDriver.IsInstalled()
            ? "Virtual controller driver: installed"
            : "Virtual controller driver: not installed";
        string? problem = Steering.PadProblem;
        PadProblemText.Text = problem ?? "";
        PadProblemText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Refresh()
    {
        _raceButtonRow.SetChipText(SteeringMap.TriggerLabel());
        foreach (var (id, row) in _rows)
        {
            row.SetChipText(MappingRow.ValueOf(SteeringMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(SteeringMapSource.Instance, id), null);
        }
        ShowSteering();
        ShowPadDriver();
    }
}
