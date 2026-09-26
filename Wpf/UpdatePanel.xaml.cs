using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace UnboundKeys.Wpf;

// Check for updates, as a small state machine over one card: confirm →
// checking → up to date / available → downloading → unpacking → starting.
// Every step needs a click except the two that are just waiting, and each
// of those has a Cancel. On success the new version is started and
// Launched fires, which the shell turns into a Quit of this one.
public partial class UpdatePanel
{
    public event Action? Launched;

    private CancellationTokenSource? _cts;
    private UpdateChecker.UpdateInfo? _info;
    private Action? _onPrimary;
    private Action? _onSecondary;
    private double _fraction;

    public UpdatePanel()
    {
        InitializeComponent();
        CheckButton.Click += (_, _) => ShowConfirm();
        PrimaryButton.Click += (_, _) => _onPrimary?.Invoke();
        SecondaryButton.Click += (_, _) => _onSecondary?.Invoke();
        Progress.SizeChanged += (_, _) => Fill(_fraction);
    }

    private void ShowConfirm()
    {
        Show("Check Fizzil's GitHub for a newer version?",
             "This is the only time UnboundKeys goes online. It asks which release is newest, nothing more.",
             progress: false);
        Buttons("Check", RunCheck, "Not now", Close);
    }

    private async void RunCheck()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Show("Checking…", "", progress: false);
        Buttons(null, null, "Cancel", () => cts.Cancel());
        try
        {
            _info = await UpdateChecker.CheckAsync(cts.Token);
            if (cts.IsCancellationRequested)
                return;
            if (!_info.IsNewer)
            {
                Show("You're up to date.", $"UnboundKeys {_info.Current} is the newest release.", progress: false);
                Buttons("Done", Close, null, null);
            }
            else if (!_info.HasZip)
            {
                Show($"UnboundKeys {_info.Latest} is available.",
                     $"You have {_info.Current}. That release has no download attached yet, so there is nothing to install from here.",
                     progress: false);
                Buttons("Done", Close, null, null);
            }
            else
            {
                Show($"UnboundKeys {_info.Latest} is available.",
                     $"You have {_info.Current}. The download is about {Math.Round(_info.ZipBytes / 1048576.0)} MB. " +
                     $"It unpacks into a folder beside this one, starts, and this copy quits. Your profiles and mappings carry over; the old folder stays until you delete it.",
                     progress: false);
                Buttons("Download and install", RunInstall, "Not now", Close);
            }
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            Show("Couldn't check.", Friendly(ex), progress: false);
            Buttons("Try again", RunCheck, "Close", Close);
        }
    }

    private async void RunInstall()
    {
        if (_info == null)
            return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var progress = new Progress<double>(Fill);
        Show($"Downloading UnboundKeys {_info.Latest}…", "", progress: true);
        Fill(0);
        Buttons(null, null, "Cancel", () => cts.Cancel());
        try
        {
            string zip = await UpdateChecker.DownloadAsync(_info, progress, cts.Token);
            Show("Unpacking…", "", progress: true);
            Fill(0);
            string exe = await UpdateChecker.InstallAsync(zip, _info.Latest, progress, cts.Token);
            Show($"Starting UnboundKeys {_info.Latest}…", "This copy quits as soon as the new one is running.", progress: false);
            Buttons(null, null, null, null);
            UpdateChecker.Launch(exe);
            Launched?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Show("Cancelled.", "Nothing was changed.", progress: false);
            Buttons("Download and install", RunInstall, "Close", Close);
        }
        catch (Exception ex)
        {
            Show("Couldn't install.", Friendly(ex), progress: false);
            Buttons("Try again", RunInstall, "Close", Close);
        }
    }

    private void Close()
    {
        _cts?.Cancel();
        Card.Visibility = Visibility.Collapsed;
        IdleRow.Visibility = Visibility.Visible;
    }

    private void Show(string message, string detail, bool progress)
    {
        IdleRow.Visibility = Visibility.Collapsed;
        Card.Visibility = Visibility.Visible;
        Message.Text = message;
        Detail.Text = detail;
        Detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Progress.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
    }

    // null hides a button.
    private void Buttons(string? primary, Action? onPrimary, string? secondary, Action? onSecondary)
    {
        _onPrimary = onPrimary;
        _onSecondary = onSecondary;
        PrimaryButton.Content = primary;
        PrimaryButton.Visibility = primary == null ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = secondary;
        SecondaryButton.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Fill(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        ProgressFill.Width = Progress.ActualWidth * _fraction;
    }

    private static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException => "GitHub couldn't be reached. Check the internet connection and try again.",
        TaskCanceledException => "GitHub took too long to answer. Try again in a moment.",
        _ => ex.Message,
    };
}
