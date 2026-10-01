using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// The ten spoken words laid out as a keypad — three across, "press ten"
// alone at the bottom — rather than a list that scrolls (Fizzil's ask):
// each tile is the phrase with what it sends underneath, lit in the
// accent once it's been changed from its default, same as the keyboard
// map. Above them, the fixed commands that always work.
public partial class VoicePage : IDashboardPage
{

    private readonly Dictionary<string, (Button Tile, TextBlock Mapping)> _tiles = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Voice";

    public VoicePage()
    {
        InitializeComponent();


        AddHeader("SAY \"PRESS\" AND A NUMBER", topMargin: 4);
        var hint = new TextBlock
        {
            Text = "Each tile shows the key that number sends. Click one to change it.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 6),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(hint);

        // Full rows of three, then whatever's left over — "press ten" —
        // centered beneath in a row of the same three columns, so it sits
        // under "press eight" at the same width (Fizzil's symmetry call).
        var words = KeyMap.RemappableWords;
        int inFullRows = words.Length - words.Length % 3;

        var keypad = new UniformGrid { Columns = 3, Margin = new Thickness(2, 0, 2, 0) };
        for (int i = 0; i < inFullRows; i++)
            keypad.Children.Add(BuildTile(words[i]));
        Rows.Children.Add(keypad);

        if (inFullRows < words.Length)
        {
            var lastRow = new Grid { Margin = new Thickness(2, 0, 2, 0) };
            for (int column = 0; column < 3; column++)
                lastRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int leftover = words.Length - inFullRows;
            int firstColumn = (3 - leftover) / 2;
            for (int i = inFullRows; i < words.Length; i++)
            {
                var tile = BuildTile(words[i]);
                Grid.SetColumn(tile, firstColumn + (i - inFullRows));
                lastRow.Children.Add(tile);
            }
            Rows.Children.Add(lastRow);
        }

        // Beneath the keypad (Fizzil): the words you say come first.
        AddHeader("ALWAYS AVAILABLE", topMargin: 16);
        AddCommandLine($"\"press {VoiceEngine.StopWord}\"", "releases every key being held or repeated");
        AddCommandLine($"\"press {VoiceEngine.MenuWord}\"", "shows this dashboard, or hides it again");
        AddCommandLine($"\"press {VoiceEngine.FadeWord}\"", "turns Fade on or off");

        // What the microphone just heard (Fizzil: "did it hear me?"), live
        // as the words come in, the command that fired in accent, then back
        // to an idle line a few seconds later. Under the picture (Fizzil),
        // beside the keypad rather than below it.
        AddHeader("HEARD", topMargin: 16, into: Left);
        _heardLine = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
        _heardLine.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Left.Children.Add(_heardLine);
        _heardIdleTimer.Tick += (_, _) =>
        {
            _heardIdleTimer.Stop();
            ShowHeardIdle();
        };
        ShowHeardIdle();
        VoiceHeard.Changed += (text, final, command) => Dispatcher.InvokeAsync(() => ShowHeard(text, final, command));
        ListeningMode.Changed += () => Dispatcher.InvokeAsync(() =>
        {
            if (!_heardIdleTimer.IsEnabled)
                ShowHeardIdle();
        });

        Refresh();
    }

    private readonly System.Collections.Generic.Dictionary<string, TextBlock> _badges = new();
    private TextBlock _heardLine = null!;
    private readonly System.Windows.Threading.DispatcherTimer _heardIdleTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    private void ShowHeard(string text, bool final, string? command)
    {
        _heardLine.Inlines.Clear();
        var words = new System.Windows.Documents.Run($"“{text}”");
        words.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
            command != null ? "AccentBrush" : final ? "TextPrimaryBrush" : "TextSecondaryBrush");
        _heardLine.Inlines.Add(words);
        if (command != null)
            _heardLine.Inlines.Add(new System.Windows.Documents.Run("  sent"));
        else if (!final)
            _heardLine.Inlines.Add(new System.Windows.Documents.Run("  …"));
        _heardIdleTimer.Stop();
        _heardIdleTimer.Start();
    }

    private void ShowHeardIdle()
    {
        _heardLine.Inlines.Clear();
        _heardLine.Inlines.Add(new System.Windows.Documents.Run(ListeningMode.IsPaused ? "Voice keys are paused." : "Listening. Say \"press\" and a number."));
    }

    private Button BuildTile(string word)
    {
        string spoken = $"\"press {word}\"";

        // Three centred lines (Fizzil): the words to say, the modes set
        // (see ModeGlyphs; the line folds away for a plain tap), then the
        // first key or two with "…" for the rest (see KeysLine).
        var phrase = new TextBlock { Text = spoken, FontSize = 13, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        var modes = new TextBlock { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0), Visibility = Visibility.Collapsed };
        modes.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var mapping = new TextBlock { FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(phrase);
        content.Children.Add(modes);
        content.Children.Add(mapping);
        _badges[word] = modes;

        var tile = new Button { Content = content, Height = 72, Margin = new Thickness(3), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center };
        tile.SetResourceReference(StyleProperty, "KeyCapStyle");
        tile.Click += (_, _) => EditRequested?.Invoke(KeyMapSource.Instance, word, spoken);
        // The tile has no width until it is laid out, so the keys line is
        // measured again once it has one (and whenever that changes).
        tile.SizeChanged += (_, _) => FillKeysLine(mapping, word, IsCustomized(word), tile.ActualWidth - 2 * KeysLineSidePadding);
        tile.MouseEnter += (_, _) =>
        {
            Diagram.SetSpeaking(true);
        };
        tile.MouseLeave += (_, _) => Diagram.SetSpeaking(false);

        _tiles[word] = (tile, mapping);
        return tile;
    }

    // The first key, the second too if it fits beside it, then "…" for
    // any more (Fizzil): a tile has one line for keys, and a key name cut
    // off mid-word read badly, so the second key is measured in the line's
    // font against the room the tile has and left out when it would spill
    // ("Backspace …" rather than "Backspace + Left Cl…"). The keys take
    // the tile's colour; the plus and the "…" stay grey, like the plus
    // between the mode glyphs.
    private const double KeysLineSidePadding = 8;

    private static void FillKeysLine(TextBlock target, string word, bool customized, double maxWidth)
    {
        target.Inlines.Clear();
        string keyBrush = customized ? "AccentBrush" : "TextSecondaryBrush";
        string primary = MappingRow.PrimaryOf(KeyMapSource.Instance, word);
        AddRun(target, primary, keyBrush);
        var extras = KeyMap.ExtraWords[word];
        if (extras.Count == 0)
            return;
        string second = KeyCatalog.DisplayNameFor(extras[0]);
        string more = extras.Count > 1 ? " …" : "";
        if (TextWidth(target, $"{primary} + {second}{more}") <= maxWidth)
        {
            AddRun(target, " + ", "TextSecondaryBrush");
            AddRun(target, second, keyBrush);
            if (more.Length > 0)
                AddRun(target, more, "TextSecondaryBrush");
        }
        else
            AddRun(target, " …", "TextSecondaryBrush");
    }

    private static double TextWidth(TextBlock target, string text) =>
        new System.Windows.Media.FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(target.FontFamily, target.FontStyle, target.FontWeight, target.FontStretch), target.FontSize,
            System.Windows.Media.Brushes.Black, System.Windows.Media.VisualTreeHelper.GetDpi(target).PixelsPerDip).WidthIncludingTrailingWhitespace;

    private static void AddRun(TextBlock target, string text, string brush)
    {
        var run = new System.Windows.Documents.Run(text);
        run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brush);
        target.Inlines.Add(run);
    }

    // A word counts as changed once its key, its extra keys, or how it is
    // pressed differ from a fresh install — the same test
    // VirtualKeyMap.IsCustomized applies to the keyboard's keys.
    private static bool IsCustomized(string word) =>
        KeyMap.Words[word] != KeyMap.DefaultWords[word]
        || KeyMap.ExtraWords[word].Count > 0
        || !KeyMap.Behaviors[word].IsPlainTap();

    public void Refresh()
    {
        foreach (var (word, (tile, mapping)) in _tiles)
        {
            bool customized = IsCustomized(word);
            tile.Tag = customized;
            FillKeysLine(mapping, word, customized, tile.ActualWidth - 2 * KeysLineSidePadding);
            _badges[word].Visibility = ModeGlyphs.Fill(_badges[word], KeyMap.Behaviors[word], scale: 0.75) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AddHeader(string text, double topMargin, System.Windows.Controls.Panel? into = null)
    {
        var header = new TextBlock { Text = text, Margin = new Thickness(0, topMargin, 0, 4) };
        header.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        (into ?? Rows).Children.Add(header);
    }

    private void AddCommandLine(string phrase, string effect)
    {
        var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 2, 12, 2) };
        var spoken = new System.Windows.Documents.Run(phrase);
        spoken.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        line.Inlines.Add(spoken);
        line.Inlines.Add(new System.Windows.Documents.Run("  " + effect));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(line);
    }
}
