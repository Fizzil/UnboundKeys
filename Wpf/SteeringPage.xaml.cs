using System;
using System.Collections.Generic;
using System.Windows;

namespace UnboundKeys.Wpf;

public partial class SteeringPage : IDashboardPage
{
    private readonly Dictionary<string, MappingRow> _rows = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

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

        // The switch is for the session, the stretch is saved. Changed can
        // fire from the hook's thread or a panic path's, so the page is
        // redrawn through the dispatcher.
        SteeringToggle.Click += (_, _) => Steering.SetOn(!Steering.IsOn);
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
        SteeringToggle.Tag = Steering.IsOn;
        StretchText.Text = $"{Math.Round(Steering.Stretch * 100)} %";
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
        foreach (var (id, row) in _rows)
        {
            row.SetChipText(MappingRow.ValueOf(SteeringMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(SteeringMapSource.Instance, id), null);
        }
        ShowSteering();
        ShowPadDriver();
    }
}
