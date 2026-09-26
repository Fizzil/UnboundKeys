using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace UnboundKeys.Wpf;

// RemapCardTab.cs (WinForms) ported to WPF and laid out as settings
// rows: the complete editor for one mapping (a spoken word, a mouse
// button, or an on-screen key) — its keys, Mode (Tap / Repeat / Hold),
// Duration and Infinite, per-key gaps, and Reset. Driven entirely
// through IRemapSource, so the same control serves all three. Reset All
// is the one thing it can't do alone: it raises ResetAllRequested and
// lets whatever hosts it reset every mapping.
public partial class RemapCard
{
    private readonly IRemapSource _source;
    private readonly string _id;

    internal event Action? ResetAllRequested;

    private bool _repeatOn;
    private bool _holdOn;
    private double _duration;
    private bool _infiniteOn;
    private bool _useCustomRepeatIntervals;
    // Game mode (see the XAML comment): the gap after every repeated key,
    // and priority with its hold time, all in seconds (0 = the defaults).
    private bool _gameMode;
    private double _repeatGap;
    private bool _priorityOn;
    private double _prioritySeconds;
    // One gap per key (index 0 = Key 1): how long to wait after that key
    // before the next, 0 meaning the executor's default. Rebuilt to the
    // right length rather than trusting saved data blindly — same guard
    // as RemapCardTab (WinForms).
    private List<double> _keyIntervalSeconds = new();
    private int _resetTapCount;
    private readonly DispatcherTimer _resetTapTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // internal, not public — the generated UserControl partial class
    // itself has to stay public for the XAML loader, but nothing outside
    // this assembly should be constructing one directly, and IRemapSource
    // (internal) can't appear in a public member's signature anyway.
    internal RemapCard(IRemapSource source, string id)
    {
        _source = source;
        _id = id;
        InitializeComponent();

        var behavior = _source.Behaviors[_id];
        _repeatOn = behavior.Repeat;
        _holdOn = behavior.Hold;
        _duration = behavior.DurationSeconds;
        _infiniteOn = behavior.Infinite;
        _useCustomRepeatIntervals = behavior.UseCustomRepeatIntervals;
        _repeatGap = behavior.RepeatGapSeconds;
        _priorityOn = behavior.Priority;
        _prioritySeconds = behavior.PrioritySeconds;
        _gameMode = Settings.LoadGameMode();

        int totalKeyCount = 1 + _source.ExtraWords[_id].Count;
        _keyIntervalSeconds = behavior.RepeatKeyIntervalsSeconds.Count == totalKeyCount
            ? new List<double>(behavior.RepeatKeyIntervalsSeconds)
            : new List<double>(new double[totalKeyCount]);

        _resetTapTimer.Tick += (_, _) =>
        {
            _resetTapCount = 0;
            _resetTapTimer.Stop();
        };

        RebuildKeyGroup();
        UpdateModeVisuals();
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: false);
        RebuildKeyIntervalRows();
        UpdateRepeatIntervalVisibility(animate: false);
        GameModeButton.Tag = _gameMode;
        PriorityButton.Tag = _priorityOn;
        UpdateGapText();
        UpdatePriorityText();
        SetElementVisible(GamePanel, _gameMode, animate: false);
        SetElementVisible(GapPanel, _repeatOn, animate: false);
        SetElementVisible(PriorityPanel, _priorityOn, animate: false);
        AddGameInfo();
    }

    // Every "Key N: X" string IRemapSource builds follows the same
    // "label: value" shape.
    private static (string Label, string Value) SplitKeyLabel(string full)
    {
        int i = full.IndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? (full, "") : (full[..i], full[(i + 2)..]);
    }

    // ---- Keys ----

    // Rebuilt from scratch on every change (a key rebound, an extra
    // added/removed) — "clear and re-add in the right order" rather than
    // patching rows in place.
    private void RebuildKeyGroup()
    {
        KeyGroup.Children.Clear();

        var extras = _source.ExtraWords[_id];

        AddKeyRow(_source.KeyLabelFor(_id), entry =>
        {
            _source.Rebind(_id, entry.VkCode);
            RebuildKeyGroup();
        }, onDelete: null);

        for (int i = 0; i < extras.Count; i++)
        {
            int slotIndex = i; // captured per-row, not the loop variable
            string label = $"Key {i + 2}: {KeyCatalog.DisplayNameFor(extras[i])}";
            AddKeyRow(label, entry =>
            {
                _source.SetExtraKey(_id, slotIndex, entry.VkCode);
                RebuildKeyGroup();
            }, onDelete: () =>
            {
                _source.RemoveExtraKey(_id, slotIndex);

                // Keep the per-key gaps in step: slot 0 is always Key 1, so
                // an extra at slotIndex is gap entry slotIndex + 1.
                int removedGapIndex = slotIndex + 1;
                if (removedGapIndex < _keyIntervalSeconds.Count)
                    _keyIntervalSeconds.RemoveAt(removedGapIndex);

                RebuildKeyGroup();
                RebuildKeyIntervalRows();
                UpdateRepeatIntervalVisibility(animate: true);
            });
        }

        // After the keys, not before them — an "add" belongs at the end
        // of the list it adds to. Gone once every extra slot is used.
        if (extras.Count < RemapStore.MaxExtraKeys)
        {
            var addKeyButton = new Button
            {
                Content = "+  Add key",
                Height = 36,
                Margin = new Thickness(8, 4, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            };
            addKeyButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            addKeyButton.Click += (_, _) =>
            {
                _source.AddExtraKey(_id, _source.AddKeySeed(_id));
                _keyIntervalSeconds.Add(0.0);
                RebuildKeyGroup();
                RebuildKeyIntervalRows();
                UpdateRepeatIntervalVisibility(animate: true);
            };
            KeyGroup.Children.Add(addKeyButton);
        }
    }

    // One label/chip row plus its own collapsed category picker directly
    // beneath it. onDelete adds the two-click Remove — only extra keys pass
    // it, since Key 1 can't be removed; Key 1 gets a same-width spacer
    // instead so every row's chip lines up.
    private void AddKeyRow(string fullLabel, Action<KeyCatalog.Entry> onSelect, Action? onDelete)
    {
        var (label, value) = SplitKeyLabel(fullLabel);

        var grid = new Grid { Height = 48 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var divider = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.35,
        };
        divider.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        Grid.SetColumnSpan(divider, 3);
        grid.Children.Add(divider);

        var labelText = new TextBlock { Text = label };
        labelText.SetResourceReference(StyleProperty, "RowLabelStyle");
        grid.Children.Add(labelText);

        // A fixed Width so every key's chip is the same size regardless of
        // how long the key's name is.
        var valueButton = new Button
        {
            Content = value,
            Width = 150,
            Margin = new Thickness(0, 0, 4, 0),
        };
        valueButton.SetResourceReference(StyleProperty, "OutlineButtonStyle");
        Grid.SetColumn(valueButton, 1);
        grid.Children.Add(valueButton);

        // Split view: category nav on the left, that category's individual
        // keys scrollable on the right, inline in this same card (Fizzil's
        // own feedback: hovering a category should fill the empty space
        // next to it, not spawn another window). A fixed height — matching
        // the nav column's own natural height, 8 categories × 40px — gives
        // the key panel something bounded to scroll within.
        const double categoryListHeight = 320;
        var categoryList = new Grid { Visibility = Visibility.Collapsed, ClipToBounds = true, Height = 0 };
        categoryList.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        categoryList.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bool expanded = false;
        valueButton.Click += (_, _) =>
        {
            expanded = !expanded;
            valueButton.Tag = expanded;
            AnimateHeight(categoryList, expanded, expanded ? categoryListHeight : 0);
        };

        if (onDelete != null)
        {
            var deleteButton = new Button();
            deleteButton.SetResourceReference(StyleProperty, "RemoveButtonStyle");
            ConfirmDeleteBehavior.AttachTo(deleteButton, onDelete);
            Grid.SetColumn(deleteButton, 2);
            grid.Children.Add(deleteButton);
        }
        else
        {
            var spacer = new Border { Width = 36, Margin = new Thickness(4, 0, 8, 0) };
            Grid.SetColumn(spacer, 2);
            grid.Children.Add(spacer);
        }

        KeyGroup.Children.Add(grid);
        KeyGroup.Children.Add(categoryList);

        PopulateCategoryList(categoryList, onSelect);
    }

    // Left column: one button per KeyCatalog category (Letters, Numbers,
    // ...). Right column: a scrollable list of whichever category was
    // last hovered.
    private void PopulateCategoryList(Grid categoryList, Action<KeyCatalog.Entry> onSelect)
    {
        var nav = new StackPanel();
        Grid.SetColumn(nav, 0);
        categoryList.Children.Add(nav);

        var keyListPanel = new StackPanel();
        var keyScroll = new ScrollViewer
        {
            Content = keyListPanel,
            // Hidden, not Auto — edge-hover scrolling (and the wheel, when
            // it isn't remapped) still work; this just drops the native
            // scrollbar, which read as out of place against this theme.
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(keyScroll, 1);
        categoryList.Children.Add(keyScroll);
        EdgeAutoScrollBehavior.SetEnable(keyScroll, true);

        void HoverCategory(KeyCatalog.Entry[] keys)
        {
            keyListPanel.Children.Clear();
            foreach (var entry in keys)
            {
                var keyButton = new Button
                {
                    Content = entry.DisplayName,
                    Height = 34,
                    Style = (System.Windows.Style)FindResource("QuietListButtonStyle"),
                };
                keyButton.Click += (_, _) => onSelect(entry);
                keyListPanel.Children.Add(keyButton);
            }
        }

        foreach (var (category, keys) in KeyCatalog.Groups)
        {
            var categoryButton = new Button
            {
                Content = category,
                Height = 40,
                Style = (System.Windows.Style)FindResource("CategoryNavButtonStyle"),
            };
            categoryButton.MouseEnter += (_, _) => HoverCategory(keys);
            nav.Children.Add(categoryButton);
        }

        // Something shows on first expand rather than a blank right panel
        // until the first hover.
        HoverCategory(KeyCatalog.Groups[0].Keys);
    }

    // ---- Mode ----

    private void TapButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: false, hold: false);
    private void RepeatButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: true, hold: false);
    private void HoldButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: false, hold: true);

    private void SetMode(bool repeat, bool hold)
    {
        if (_repeatOn == repeat && _holdOn == hold)
            return;
        _repeatOn = repeat;
        _holdOn = hold;
        UpdateModeVisuals();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        UpdateRepeatIntervalVisibility(animate: true);
        SetElementVisible(GapPanel, _repeatOn, animate: true);
        SaveBehavior();
    }

    private void UpdateModeVisuals()
    {
        TapButton.Tag = !_repeatOn && !_holdOn;
        RepeatButton.Tag = _repeatOn;
        HoldButton.Tag = _holdOn;
        ModeHint.Text = _holdOn ? "Keeps the keys pressed for the duration below."
            : _repeatOn ? "Taps the keys again and again for the duration below."
            : "Presses the keys once each time.";
    }

    // ---- Duration / Infinite ----

    private void PlusOneButton_Click(object sender, RoutedEventArgs e) => AdjustDuration(1.0);
    private void PlusTenthButton_Click(object sender, RoutedEventArgs e) => AdjustDuration(0.1);

    private void AdjustDuration(double delta)
    {
        _duration = Math.Round(_duration + delta, 1);
        _infiniteOn = false;
        InfiniteButton.Tag = false;
        UpdateDurationText();
        SaveBehavior();
    }

    private void ResetDurationButton_Click(object sender, RoutedEventArgs e)
    {
        _duration = 0.0;
        UpdateDurationText();
        SaveBehavior();
    }

    private void InfiniteButton_Click(object sender, RoutedEventArgs e)
    {
        _infiniteOn = !_infiniteOn;
        InfiniteButton.Tag = _infiniteOn;
        if (_infiniteOn)
            _duration = 0.0;
        UpdateDurationText();
        SaveBehavior();
    }

    private void UpdateDurationText() =>
        DurationText.Text = _infiniteOn ? "∞" : $"{_duration.ToString("0.0", CultureInfo.InvariantCulture)} s";

    // ---- Per-key gaps ----

    private void RepeatIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        _useCustomRepeatIntervals = !_useCustomRepeatIntervals;
        RepeatIntervalButton.Tag = _useCustomRepeatIntervals;
        SaveBehavior();
        UpdateRepeatIntervalVisibility(animate: true);
    }

    // One row per key: "After Key N … 0.3 s [+0.1 s] [+1 s] [Reset]".
    // Rebuilt whenever a key is added/removed or a gap changes.
    private void RebuildKeyIntervalRows()
    {
        KeyIntervalRows.Children.Clear();

        for (int i = 0; i < _keyIntervalSeconds.Count; i++)
        {
            int index = i; // captured per-row, not the loop variable

            var row = new Grid { Height = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Indented under the switch it belongs to (RowLabelStyle's own
            // margin is 12; a local value outranks the style's).
            var label = new TextBlock { Text = $"After Key {index + 1}", Margin = new Thickness(28, 0, 0, 0) };
            label.SetResourceReference(StyleProperty, "RowLabelStyle");
            row.Children.Add(label);

            var value = new TextBlock { Text = GapText(_keyIntervalSeconds[index]) };
            value.SetResourceReference(StyleProperty, "ValueTextStyle");
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            var buttons = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            buttons.Children.Add(StepButton("+0.1 s", () => SetGap(index, Math.Round(_keyIntervalSeconds[index] + 0.1, 1))));
            buttons.Children.Add(StepButton("+1 s", () => SetGap(index, Math.Round(_keyIntervalSeconds[index] + 1.0, 1))));
            buttons.Children.Add(StepButton("Reset", () => SetGap(index, 0.0)));
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);

            KeyIntervalRows.Children.Add(row);
        }
    }

    // 0 isn't "no gap" — it's "the executor's usual 0.1 s" (see
    // KeyExecutor.GapMsAfterKey), so say so instead of showing 0.0 s.
    private string GapText(double seconds) =>
        seconds > 0 ? $"{seconds.ToString("0.0", CultureInfo.InvariantCulture)} s"
        : _repeatGap > 0 ? $"gap ({_repeatGap.ToString("0.0", CultureInfo.InvariantCulture)} s)"
        : "default (0.1 s)";

    private void SetGap(int index, double seconds)
    {
        _keyIntervalSeconds[index] = seconds;
        RebuildKeyIntervalRows();
        SaveBehavior();
    }

    private static Button StepButton(string text, Action onClick)
    {
        var button = new Button { Content = text };
        button.SetResourceReference(StyleProperty, "StepButtonStyle");
        button.Click += (_, _) => onClick();
        return button;
    }

    // Custom gaps only apply to Repeat with 2+ keys; if that stops being
    // true the feature switches itself off — same guard as RemapCardTab's
    // canCustomizeRepeatInterval (WinForms).
    private void UpdateRepeatIntervalVisibility(bool animate)
    {
        bool canCustomize = _repeatOn && _source.ExtraWords[_id].Count >= 1;
        if (!canCustomize && _useCustomRepeatIntervals)
            _useCustomRepeatIntervals = false;
        RepeatIntervalButton.Tag = _useCustomRepeatIntervals;

        SetElementVisible(GapsPanel, canCustomize, animate);
        SetElementVisible(KeyIntervalRows, canCustomize && _useCustomRepeatIntervals, animate);
    }

    // ---- Game mode ----

    private void GameModeButton_Click(object sender, RoutedEventArgs e)
    {
        _gameMode = !_gameMode;
        GameModeButton.Tag = _gameMode;
        Settings.SaveGameMode(_gameMode);
        SetElementVisible(GamePanel, _gameMode, animate: true);
    }

    private void GapOneButton_Click(object sender, RoutedEventArgs e) => SetRepeatGap(1.0);
    private void GapOneAndHalfButton_Click(object sender, RoutedEventArgs e) => SetRepeatGap(1.5);
    private void GapPlusTenthButton_Click(object sender, RoutedEventArgs e) => SetRepeatGap(Math.Round(_repeatGap + 0.1, 1));
    private void GapResetButton_Click(object sender, RoutedEventArgs e) => SetRepeatGap(0.0);

    private void SetRepeatGap(double seconds)
    {
        _repeatGap = seconds;
        UpdateGapText();
        RebuildKeyIntervalRows(); // their "default" wording follows the gap
        SaveBehavior();
    }

    private void UpdateGapText() => RepeatGapText.Text = GapText(_repeatGap);

    private void PriorityButton_Click(object sender, RoutedEventArgs e)
    {
        _priorityOn = !_priorityOn;
        PriorityButton.Tag = _priorityOn;
        SetElementVisible(PriorityPanel, _priorityOn, animate: true);
        SaveBehavior();
    }

    private void PriorityPlusTenthButton_Click(object sender, RoutedEventArgs e) => SetPrioritySeconds(Math.Round(_prioritySeconds + 0.1, 1));
    private void PriorityPlusOneButton_Click(object sender, RoutedEventArgs e) => SetPrioritySeconds(Math.Round(_prioritySeconds + 1.0, 1));
    private void PriorityResetButton_Click(object sender, RoutedEventArgs e) => SetPrioritySeconds(0.0);

    private void SetPrioritySeconds(double seconds)
    {
        _prioritySeconds = seconds;
        UpdatePriorityText();
        SaveBehavior();
    }

    // 0 means one global cooldown (see KeyBehavior.PrioritySeconds).
    private void UpdatePriorityText() =>
        PriorityText.Text = _prioritySeconds <= 0 ? "one GCD" : $"{_prioritySeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";

    // The Help page lines for game mode, here as well, under a fold.
    private void AddGameInfo()
    {
        AddInfoLine("Gap between keys", "How long an infinite repeat waits after each key. Set it to your global cooldown (GCD): 1.5 s for most WoW classes (a little less with haste), 1.0 s for Rogues, cat-form Druids and Monks.");
        AddInfoLine("Priority", "A priority key can interrupt an infinite repeat. Press it and the repeat pauses, the app waits for the current global cooldown to finish, your key fires, and the repeat stays paused for the time set below.");
        AddInfoLine("Pause the repeat for", "Reset pauses the infinite repeat for one GCD. Change the pause time for a channelled ability, usually two to three seconds.");
        AddInfoLine("Two priority keys in a row", "extend the pause; the second never cuts the first short.");
        AddInfoLine("Where it applies", "Any mapping can be a priority key: a spoken word, a mouse button, an on-screen key or a remapped real key. Game mode only shows these rows; the settings work even with it off.");
    }

    private void AddInfoLine(string lead, string explanation)
    {
        var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 3, 12, 3) };
        var leadRun = new System.Windows.Documents.Run(lead);
        leadRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        line.Inlines.Add(leadRun);
        line.Inlines.Add(new System.Windows.Documents.Run("  " + explanation));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        GameInfoLines.Children.Add(line);
    }

    // ---- Save / Reset ----

    private void SaveBehavior() =>
        _source.SetBehavior(_id, _repeatOn, _holdOn, _duration, _infiniteOn,
            _useCustomRepeatIntervals, _keyIntervalSeconds, _repeatGap, _priorityOn, _prioritySeconds);

    // Resets everything about this mapping: the key(s), Mode, Infinite,
    // the duration, and every gap.
    private void ResetCard()
    {
        _source.ResetToDefault(_id); // also clears extra keys (and, for a mouse button, disables it)
        RebuildKeyGroup();

        var behavior = _source.Behaviors[_id];
        _repeatOn = behavior.Repeat;
        _holdOn = behavior.Hold;
        _duration = behavior.DurationSeconds;
        _infiniteOn = behavior.Infinite;
        _keyIntervalSeconds = new List<double> { 0.0 }; // only the primary key remains after reset
        _useCustomRepeatIntervals = false;
        _repeatGap = 0.0;
        _priorityOn = false;
        _prioritySeconds = 0.0;

        UpdateModeVisuals();
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        RebuildKeyIntervalRows();
        UpdateRepeatIntervalVisibility(animate: true);
        PriorityButton.Tag = false;
        UpdateGapText();
        UpdatePriorityText();
        SetElementVisible(GapPanel, _repeatOn, animate: true);
        SetElementVisible(PriorityPanel, false, animate: true);
        ResetAllButton.Visibility = Visibility.Collapsed;
    }

    // Easter egg kept from the WinForms card: three taps within 600ms
    // reveal "Reset every mapping" next to it. Every tap resets THIS
    // mapping immediately regardless.
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _resetTapCount++;
        _resetTapTimer.Stop();
        _resetTapTimer.Start();

        ResetCard();

        if (_resetTapCount == 3)
        {
            _resetTapCount = 0;
            ResetAllButton.Visibility = Visibility.Visible;
        }
    }

    // This card resets itself; the host (DashboardShell) resets every
    // other mapping in response to the event.
    private void ResetAllButton_Click(object sender, RoutedEventArgs e)
    {
        ResetCard();
        ResetAllRequested?.Invoke();
    }

    // ---- Show/hide with animation ----

    // What each section is meant to be right now, by intent rather than by
    // its current Visibility — a section mid-collapse is still Visible,
    // and a request to show it again must not be mistaken for "already
    // shown". Every caller passes the full desired state, so a repeat of
    // the same intent is skipped instead of replaying the animation.
    private readonly Dictionary<FrameworkElement, bool> _sectionShown = new();

    private void SetElementVisible(FrameworkElement element, bool visible, bool animate)
    {
        if (animate && _sectionShown.TryGetValue(element, out bool shown) && shown == visible)
            return;
        _sectionShown[element] = visible;

        if (animate)
        {
            AnimateHeight(element, visible);
        }
        else if (visible)
        {
            element.BeginAnimation(HeightProperty, null);
            element.Visibility = Visibility.Visible;
            element.ClearValue(HeightProperty);
        }
        else
        {
            element.Visibility = Visibility.Collapsed;
        }
    }

    // Grows/shrinks an element's Height instead of snapping straight to
    // its final size. A plain FrameworkElement, not a specific panel type,
    // since the sections are a mix of Grids and StackPanels.
    //
    // A finished DoubleAnimation keeps HOLDING its last value on the
    // property (FillBehavior.HoldEnd), outranking any Height set in code —
    // so a collapsed section stayed pinned at 0 and the next expand
    // measured it as 0 tall and animated from 0 to 0 (the "Duration
    // disappeared after Reset" bug). Every path here therefore removes
    // the previous animation (BeginAnimation(…, null)) before measuring,
    // and again once it completes, so the element goes back to sizing
    // itself naturally and can grow with its content afterward.
    private void AnimateHeight(FrameworkElement element, bool expand)
    {
        if (expand)
        {
            element.BeginAnimation(HeightProperty, null);
            element.Visibility = Visibility.Visible;
            element.ClearValue(HeightProperty);
            // Before the first layout pass ActualWidth is 0 (the card has
            // no fixed Width any more) and a NaN measure throws —
            // unconstrained is the safe fallback.
            element.Measure(new Size(ActualWidth > 0 ? ActualWidth : double.PositiveInfinity, double.PositiveInfinity));
            double targetHeight = element.DesiredSize.Height;

            var anim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            anim.Completed += (_, _) => element.BeginAnimation(HeightProperty, null);
            element.BeginAnimation(HeightProperty, anim);
        }
        else
        {
            double currentHeight = element.ActualHeight;
            var anim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(HeightProperty, null);
            };
            element.BeginAnimation(HeightProperty, anim);
        }
    }

    // Same animation, but to a caller-given height instead of measuring
    // the element's own natural size — used for the category split view,
    // which is a fixed height by design (see AddKeyRow's own comment).
    // Here the hold at the target height is wanted, so only the collapse
    // releases it.
    private void AnimateHeight(FrameworkElement element, bool expand, double explicitTargetHeight)
    {
        if (expand)
        {
            element.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, explicitTargetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            element.BeginAnimation(HeightProperty, anim);
        }
        else
        {
            double currentHeight = element.ActualHeight;
            var anim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) =>
            {
                element.Visibility = Visibility.Collapsed;
                element.BeginAnimation(HeightProperty, null);
            };
            element.BeginAnimation(HeightProperty, anim);
        }
    }
}
