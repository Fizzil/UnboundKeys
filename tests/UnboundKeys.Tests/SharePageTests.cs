using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UnboundKeys.Wpf;
// The harness also references Windows Forms, which has its own of these.
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace UnboundKeys.Tests;

// Settings > Profiles > Share and Import profile, on the real page: it is
// built off-screen with the app's own theme, its buttons are clicked the
// way the mouse would, and what it wrote is checked. Settings go to the
// scratch file and the shared files to scratch folders (see
// ProfileShare.UseScratchFolders), so nothing here reaches Documents or
// Downloads and no Explorer window opens.
//
// With a pictures folder (dotnet run ... -- --pictures <folder>) the page
// is also saved as PNGs at each step, for looking at.
internal static class SharePageTests
{
    private static bool _ok;

    public static bool Run(string scratchDir, string? pictures)
    {
        _ok = true;
        Console.WriteLine("--- the Settings page: Share and Import profile ---");
        Exception? failure = null;
        // WPF controls must be made and used on one STA thread.
        var thread = new Thread(() =>
        {
            try { Body(scratchDir, pictures); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            Check(false, $"threw {failure.GetType().Name}: {failure.Message}");
        return _ok;
    }

    private static void Body(string scratchDir, string? pictures)
    {
        string settings = Path.Combine(scratchDir, "settings.json");
        File.Delete(settings);
        File.Delete(settings + ".bak");
        string share = Path.Combine(scratchDir, "page-share");
        string downloads = Path.Combine(scratchDir, "page-downloads");
        Directory.CreateDirectory(downloads);
        ProfileShare.UseScratchFolders(share, downloads);

        Settings.CreateProfileIfMissing("WoW", new() { ["one"] = 0x41 }, new(), new(), new(), new(), new(), new());
        Settings.CreateSubProfile("WoW", "Hunter");
        Settings.CreateProfileIfMissing("the isle", new(), new(), new(), new(), new(), new(), new());

        // The app's own look (see Program.cs), so the pictures are honest.
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The app's file carries its version in its name, so it is asked for.
        string appName = typeof(Settings).Assembly.GetName().Name!;
        foreach (string theme in new[] { "Theme.Red", "Theme.Controls" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/{appName};component/Themes/{theme}.xaml", UriKind.Relative) });

        var page = new SettingsPage();
        page.Refresh();
        page.ProfilesFold.IsOpen = true;
        Picture(page, pictures, "share-1-profiles");

        Check(Buttons(page, "Share").Count == 3, "every profile row has a Share button, Default too");
        Check(Buttons(page, "Rename").Count(b => b.Visibility == Visibility.Visible) >= 2, "Rename is still there");
        Check(Buttons(page, "Import profile").Count == 1, "and there is one Import profile button");

        // Share: the second row is WoW.
        Click(Buttons(page, "Share")[1]);
        string file = Path.Combine(share, "WoW.unboundkeys");
        Check(File.Exists(file), "Share writes the profile's file to the share folder");
        Check(Notes(page).Any(n => n.Contains("Saved WoW.unboundkeys")), "and says where it went");
        Picture(page, pictures, "share-2-shared");

        // Import, with nothing in Downloads but our own shared file.
        Click(Buttons(page, "Import profile")[0]);
        Check(Buttons(page, "Close import list").Count == 1, "Import profile opens the list, and the same button closes it");
        Check(Buttons(page, "WoW").Count == 2, "the shared file is offered by its profile name");
        Picture(page, pictures, "share-3-import-list");

        // The offered file is the last "WoW" button; the first is the row.
        Click(Buttons(page, "WoW")[1]);
        Check(Settings.LoadProfileNames().SequenceEqual(new[] { "Default", "WoW", "the isle", "WoW 2" }), "clicking it adds the profile under a free name");
        Check(Settings.LoadSubProfileNames("WoW 2").SequenceEqual(new[] { "Default", "Hunter" }), "with its sub-profiles");
        Check(Buttons(page, "Import profile").Count == 1, "the list closes");
        Check(Notes(page).Any(n => n.Contains("Imported as WoW 2")), "and the page says what it was imported as");
        Check(Buttons(page, "Share").Count == 4, "the new profile is in the list");
        Picture(page, pictures, "share-4-imported");

        // A file that is not a profile never shows; an empty list says so.
        File.Delete(file);
        File.WriteAllText(Path.Combine(downloads, "junk.unboundkeys"), "nope");
        Click(Buttons(page, "Import profile")[0]);
        Check(Notes(page).Any(n => n.Contains("No profile files found")), "with nothing to import, the list says so");
        Picture(page, pictures, "share-5-nothing-found");
        Click(Buttons(page, "Close import list")[0]);
        Check(!Notes(page).Any(n => n.Contains("No profile files found")), "and closing it takes the line away");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    // Every button in the profile list whose text (or first line of text)
    // is exactly this, top to bottom.
    private static List<Button> Buttons(SettingsPage page, string text) =>
        Descendants<Button>(page.ProfileList).Where(b => TextOf(b.Content) == text).ToList();

    // The plain lines of text in the profile list that are not on a button.
    private static List<string> Notes(SettingsPage page) =>
        page.ProfileList.Children.OfType<TextBlock>().Select(t => t.Text).ToList();

    private static string TextOf(object? content) => content switch
    {
        string s => s,
        TextBlock t => t.Text,
        Panel p => p.Children.OfType<TextBlock>().Select(t => t.Text).FirstOrDefault() ?? "",
        _ => "",
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
                continue;
            if (node is T match)
                yield return match;
            // A button's own content is its label, not more of the list.
            if (node is not Button)
                foreach (var deeper in Descendants<T>(node))
                    yield return deeper;
        }
    }

    private static void Picture(SettingsPage page, string? folder, string name)
    {
        if (folder == null)
            return;
        Directory.CreateDirectory(folder);
        const double width = 560;
        // Only the PROFILES section: the page is long and the rest has not
        // changed.
        var section = page.ProfilesFold;
        page.Measure(new Size(width, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();

        var size = section.RenderSize;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("BackgroundBrush"), null, new Rect(size));
            context.DrawRectangle(new VisualBrush(section) { Stretch = Stretch.None }, null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}
