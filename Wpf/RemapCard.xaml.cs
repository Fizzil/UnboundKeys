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
    private bool _rotation; // Repeat as priority bursts (see KeyBehavior.Rotation)
    // Infinite pause (see the XAML comment): every infinite repeat pauses
    // for _prioritySeconds while this key fires.
    private bool _priorityOn;
    private double _prioritySeconds;
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
        _rotation = behavior.Rotation;
        _priorityOn = behavior.Priority;
        _prioritySeconds = behavior.PrioritySeconds;

        // A priority key saved before Infinite pause existed, with "one gap"
        // (0) as its pause, reads as the 1.0 s start.
        if (_priorityOn && _prioritySeconds < 0.1)
        {
            _prioritySeconds = DefaultPauseSeconds;
            SaveBehavior();
        }

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
        RefreshPauseRows(animate: false);

        // Only a mouse button or a Keyboard-page key has another way of
        // pressing to reset along with this one (see Gestures).
        ResetBothButton.Visibility = source is MouseMapSource or VirtualKeyMapSource ? Visibility.Visible : Visibility.Collapsed;
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
                RebuildKeyGroup();
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
                RebuildKeyGroup();
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
            // Once open, the whole picker comes on screen: near the bottom of
            // a page its own bottom edge (the edge-hover scroll zone) could
            // sit below the window with no way to reach it.
            AnimateHeight(categoryList, expanded, expanded ? categoryListHeight : 0,
                onExpanded: () => categoryList.BringIntoView());
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

        PopulateCategoryList(categoryList, onSelect, value);
    }

    // Left column: one row per KeyCatalog category (Letters, Numbers,
    // ...), each a small key cap with a sample glyph and the name, in the
    // rail's own pill style; the category whose keys are showing keeps
    // its pill and its sample turns accent. Right column: that category's
    // keys, the one this mapping currently sends in accent. Opens on the
    // current key's category, so what is set is the first thing seen.
    private void PopulateCategoryList(Grid categoryList, Action<KeyCatalog.Entry> onSelect, string currentKeyName)
    {
        var nav = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(nav, 0);
        categoryList.Children.Add(nav);

        var keyListPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
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

        Button? activeCategory = null;
        TextBlock? activeSample = null;

        void ShowCategory(Button categoryButton, TextBlock sample, KeyCatalog.Entry[] keys)
        {
            if (activeCategory != null)
            {
                activeCategory.Tag = false;
                activeSample!.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
            activeCategory = categoryButton;
            activeSample = sample;
            categoryButton.Tag = true;
            sample.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            keyListPanel.Children.Clear();
            foreach (var entry in keys)
            {
                var keyButton = new Button { Content = entry.DisplayName, Tag = entry.DisplayName == currentKeyName };
                keyButton.SetResourceReference(StyleProperty, "PickerKeyButtonStyle");
                keyButton.Click += (_, _) => onSelect(entry);
                keyListPanel.Children.Add(keyButton);
            }
        }

        Button? first = null;
        TextBlock? firstSample = null;
        KeyCatalog.Entry[]? firstKeys = null;
        foreach (var (category, keys) in KeyCatalog.Groups)
        {
            var sample = new TextBlock
            {
                Text = SampleFor(category),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (category == "Mouse")
            {
                // The rail's own mouse glyph.
                sample.FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
                sample.FontSize = 13;
                sample.FontWeight = FontWeights.Normal;
            }
            sample.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var cap = new Border { Width = 30, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0), Child = sample };
            cap.SetResourceReference(Border.BackgroundProperty, "HoverBrush");

            var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            content.Children.Add(cap);
            content.Children.Add(new TextBlock { Text = category, VerticalAlignment = VerticalAlignment.Center });

            var categoryButton = new Button { Content = content };
            categoryButton.SetResourceReference(StyleProperty, "CategoryNavButtonStyle");
            var keysHere = keys;
            categoryButton.MouseEnter += (_, _) => ShowCategory(categoryButton, sample, keysHere);
            nav.Children.Add(categoryButton);

            // The first category is the fallback; the one holding the
            // current key wins.
            if (first == null || keys.Any(k => k.DisplayName == currentKeyName))
            {
                first = categoryButton;
                firstSample = sample;
                firstKeys = keys;
            }
        }

        ShowCategory(first!, firstSample!, firstKeys!);
    }

    // What each category's key cap shows.
    private static string SampleFor(string category) => category switch
    {
        "Letters" => "A",
        "Numbers" => "7",
        "Function Keys" => "F5",
        "Navigation" => "→",
        "Modifiers" => "Alt",
        "Mouse" => "",
        _ => "Esc",
    };

    // ---- Mode ----

    private void TapButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: false, hold: false);
    private void RepeatButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: true, hold: false);
    private void RotationButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: true, hold: false, rotation: true);
    private void HoldButton_Click(object sender, RoutedEventArgs e) => SetMode(repeat: false, hold: true);

    // Rotation is Repeat with the burst pattern, so it shares Repeat's
    // rows (Duration, Infinite, Infinite pause).
    private void SetMode(bool repeat, bool hold, bool rotation = false)
    {
        // Infinite and Infinite pause live under the timing rows, so Tap
        // (which hides them) switches both off rather than leaving them set
        // out of sight: an unseen Infinite made a "Tap" hold its key for
        // good, since the executor looks at Infinite first. A second click
        // on Tap clears them too, for a mapping saved that way before.
        bool tap = !repeat && !hold;
        bool clearing = tap && (_infiniteOn || _priorityOn);
        if (_repeatOn == repeat && _holdOn == hold && _rotation == rotation && !clearing)
            return;
        _repeatOn = repeat;
        _holdOn = hold;
        _rotation = rotation;
        UpdateModeVisuals();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        if (tap && _infiniteOn)
        {
            _infiniteOn = false;
            InfiniteButton.Tag = false;
            UpdateDurationText();
        }
        if (tap && _priorityOn)
        {
            _priorityOn = false;
            _prioritySeconds = 0.0;
            RefreshPauseRows(animate: true);
        }
        SaveBehavior();
    }

    private void UpdateModeVisuals()
    {
        TapButton.Tag = !_repeatOn && !_holdOn;
        RepeatButton.Tag = _repeatOn && !_rotation;
        RotationButton.Tag = _repeatOn && _rotation;
        HoldButton.Tag = _holdOn;
        ModeHint.Text = _holdOn ? "Holds all the keys down together for the duration below, then lets go. Repeats while held, like a key held on a real keyboard."
            : _repeatOn && _rotation ? "About ten times a second, presses Key 1, Key 2, Key 3 and so on in a quick row. The game uses the first one that's ready and ignores the rest, so put your most important ability on Key 1. Leave off anything you want to time yourself."
            : _repeatOn ? "Presses the keys one at a time, ten times a second: Key 1, Key 2 and so on, then back to Key 1, for the duration below. Never together, so use Tap for shortcuts like Ctrl + X."
            : "Presses all the keys together, once. Right for shortcuts like Ctrl + X.";
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

    // Infinite and Infinite pause are either/or (Fizzil): the pause key is
    // the one that cuts into infinite repeats, so it cannot itself be one.
    // Switching either on switches the other off.
    private void InfiniteButton_Click(object sender, RoutedEventArgs e)
    {
        _infiniteOn = !_infiniteOn;
        InfiniteButton.Tag = _infiniteOn;
        if (_infiniteOn)
        {
            _duration = 0.0;
            if (_priorityOn)
            {
                _priorityOn = false;
                _prioritySeconds = 0.0;
                RefreshPauseRows(animate: true);
            }
        }
        UpdateDurationText();
        SaveBehavior();
    }

    private void UpdateDurationText() =>
        DurationText.Text = _infiniteOn ? "∞" : $"{_duration.ToString("0.0", CultureInfo.InvariantCulture)} s";

    // ---- Infinite pause ----
    //
    // Priority and Channelled ability as one switch and one number
    // (Fizzil, playtesting): every infinite repeat pauses for
    // PrioritySeconds while this key fires. The key's own Mode stays
    // separate; a Hold of the same length is Fizzil's recipe for an
    // ability that must land whatever the cooldown.
    private const double DefaultPauseSeconds = 1.0;

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _priorityOn = !_priorityOn;
        _prioritySeconds = _priorityOn ? (_prioritySeconds >= 0.1 ? _prioritySeconds : DefaultPauseSeconds) : 0.0;
        if (_priorityOn && _infiniteOn)
        {
            // Either/or with Infinite (see InfiniteButton_Click).
            _infiniteOn = false;
            InfiniteButton.Tag = false;
            UpdateDurationText();
        }
        RefreshPauseRows(animate: true);
        SaveBehavior();
    }

    private void PauseMinusTenthButton_Click(object sender, RoutedEventArgs e) => SetPauseSeconds(Math.Round(_prioritySeconds - 0.1, 1));
    private void PausePlusTenthButton_Click(object sender, RoutedEventArgs e) => SetPauseSeconds(Math.Round(_prioritySeconds + 0.1, 1));
    private void PauseResetButton_Click(object sender, RoutedEventArgs e) => SetPauseSeconds(DefaultPauseSeconds);

    private void SetPauseSeconds(double seconds)
    {
        _prioritySeconds = Math.Max(0.1, seconds);
        RefreshPauseRows(animate: false);
        SaveBehavior();
    }

    private void RefreshPauseRows(bool animate)
    {
        PauseButton.Tag = _priorityOn;
        PauseText.Text = $"{_prioritySeconds.ToString("0.0", CultureInfo.InvariantCulture)} s";
        SetElementVisible(PausePanel, _priorityOn, animate);
    }

    private void SaveBehavior() =>
        _source.SetBehavior(_id, new KeyBehavior
        {
            Repeat = _repeatOn,
            Hold = _holdOn,
            DurationSeconds = _duration,
            Infinite = _infiniteOn,
            Rotation = _rotation,
            Priority = _priorityOn,
            PrioritySeconds = _prioritySeconds,
        });

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
        _rotation = behavior.Rotation;
        _priorityOn = false;
        _prioritySeconds = 0.0;

        UpdateModeVisuals();
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        RefreshPauseRows(animate: true);
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

    // "Reset both" (Fizzil): this mapping and the other way of pressing the
    // same button or key, in one click. The other way's card is not on
    // screen; a fresh one reads the reset state when its segment is picked.
    private void ResetBothButton_Click(object sender, RoutedEventArgs e)
    {
        _source.ResetToDefault(Gestures.Sibling(_id));
        ResetCard();
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
    private void AnimateHeight(FrameworkElement element, bool expand, double explicitTargetHeight, Action? onExpanded = null)
    {
        if (expand)
        {
            element.Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(0, explicitTargetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            if (onExpanded != null)
                anim.Completed += (_, _) => onExpanded();
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
