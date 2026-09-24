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

// RemapCardTab.cs (WinForms) ported to WPF: the complete editor for one
// mapping (a spoken word, a mouse button, or an on-screen key) — its
// keys, Repeat/Hold, timing, per-key repeat gaps, and Reset. Driven
// entirely through IRemapSource, so the same control serves all three.
// Reset All is the one thing it can't do alone: it raises
// ResetAllRequested and lets whatever hosts it reset every mapping.
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
    // One gap value per key (index 0 = K1, the primary key) — same
    // "guard against stale/mismatched saved data" reasoning as
    // RemapCardTab's own keyIntervalSeconds (WinForms): rebuilt to the
    // right length rather than trusting a saved value blindly.
    private List<double> _keyIntervalSeconds = new();
    // -1 = no key selected -> +1/+0.1/reset target the main duration.
    private int _selectedKIndex = -1;
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
        RepeatButton.Tag = _repeatOn;
        HoldButton.Tag = _holdOn;
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: false);
        RebuildKeyIntervalRow();
        UpdateRepeatIntervalVisibility(animate: false);
    }

    // Every "Key N: X" string IRemapSource builds follows the same
    // "label: value" shape — same convention as RemapCardTab.cs's own
    // SplitKeyLabel (WinForms), reimplemented here rather than shared
    // across the two UIs mid-migration.
    private static (string Label, string Value) SplitKeyLabel(string full)
    {
        int i = full.IndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? (full, "") : (full[..i], full[(i + 2)..]);
    }

    // Rebuilt from scratch on every change (a key rebound, an extra
    // added/removed) — same "clear and re-add in the right order" pattern
    // as RemapCardTab's own RebuildList/RebuildExtraKeyRows (WinForms),
    // rather than trying to patch individual rows in place.
    private void RebuildKeyGroup()
    {
        KeyGroup.Children.Clear();

        var extras = _source.ExtraWords[_id];
        bool atExtraKeyCap = extras.Count >= 2;

        if (!atExtraKeyCap)
        {
            var addKeyButton = new Button
            {
                Content = "Add Key",
                Height = 40,
                Style = (System.Windows.Style)FindResource("ListButtonStyle"),
            };
            addKeyButton.Click += (_, _) =>
            {
                _source.AddExtraKey(_id, _source.AddKeySeed(_id));
                _keyIntervalSeconds.Add(0.0);
                RebuildKeyGroup();
                RebuildKeyIntervalRow();
                UpdateRepeatIntervalVisibility(animate: true);
            };
            KeyGroup.Children.Add(addKeyButton);
        }

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

                // Keep the repeat-interval gaps in sync: slot 0 is always
                // the primary key, so an extra at slotIndex is K-slot
                // (slotIndex + 1). Matches RemapCardTab's own delete
                // handler (WinForms).
                int removedKIndex = slotIndex + 1;
                if (removedKIndex < _keyIntervalSeconds.Count)
                    _keyIntervalSeconds.RemoveAt(removedKIndex);
                if (_selectedKIndex == removedKIndex)
                    _selectedKIndex = -1;
                else if (_selectedKIndex > removedKIndex)
                    _selectedKIndex--;

                RebuildKeyGroup();
                RebuildKeyIntervalRow();
                UpdateRepeatIntervalVisibility(animate: true);
            });
        }
    }

    // One label/value row plus its own collapsed category list directly
    // beneath it. onDelete adds a third column with an always-visible
    // confirm-delete "✕" — only extra keys pass it, since Key 1 can't be
    // removed. Matches RemapCardTab's own BuildKeyLabelRow (WinForms).
    private void AddKeyRow(string fullLabel, Action<KeyCatalog.Entry> onSelect, Action? onDelete)
    {
        var (label, value) = SplitKeyLabel(fullLabel);

        var grid = new Grid { Height = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(onDelete == null ? 50 : 40, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(onDelete == null ? 50 : 40, GridUnitType.Star) });
        if (onDelete != null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });

        // A hairline under the row and Muted/plain-weight label text,
        // matching Mouse Keys' own rows (Fizzil's own request to carry
        // that look here) rather than LabelDisplayStyle's brighter 16pt
        // default.
        var divider = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.35,
        };
        divider.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        Grid.SetColumnSpan(divider, grid.ColumnDefinitions.Count);
        grid.Children.Add(divider);

        var labelText = new TextBlock
        {
            Text = label,
            Style = (System.Windows.Style)FindResource("LabelDisplayStyle"),
            Background = System.Windows.Media.Brushes.Transparent,
            FontSize = 13,
            FontWeight = System.Windows.FontWeights.Normal,
        };
        labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        // OutlineButtonStyle, not ValueButtonStyle — same "small bordered
        // chip, no big fill block" treatment as Mouse Keys' own rows,
        // including the fixed Width so every key's value chip lines up
        // the same size regardless of how long the key's name is.
        var valueButton = new Button
        {
            Content = value,
            Width = 150,
            Style = (System.Windows.Style)FindResource("OutlineButtonStyle"),
        };
        Grid.SetColumn(valueButton, 1);
        grid.Children.Add(valueButton);

        // Split view: category nav on the left, that category's individual
        // keys scrollable on the right, both inline in this same window —
        // replaces the earlier separate floating CategoryKeyPopup (Fizzil's
        // own feedback: hovering a category should fill the empty space
        // next to it, not spawn another window). A fixed height (matching
        // the nav column's own natural height — 8 categories × 40px) gives
        // the key panel something bounded to scroll within, rather than
        // growing to fit however many keys the longest category has.
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
            var deleteButton = new Button
            {
                Content = "✕",
                Style = (System.Windows.Style)FindResource("ToggleFillButtonStyle"),
            };
            ConfirmDeleteBehavior.AttachTo(deleteButton, onDelete);
            Grid.SetColumn(deleteButton, 2);
            grid.Children.Add(deleteButton);
        }

        KeyGroup.Children.Add(grid);
        KeyGroup.Children.Add(categoryList);

        PopulateCategoryList(categoryList, onSelect);
    }

    // Left column: one button per KeyCatalog category (Letters, Numbers,
    // ...). Right column: a scrollable list of whichever category was
    // last hovered — filled in by HoverCategory below rather than a
    // separate floating CategoryKeyPopup window (Fizzil's own feedback:
    // hovering a category should fill the empty space next to it, inline,
    // not spawn another window).
    private void PopulateCategoryList(Grid categoryList, Action<KeyCatalog.Entry> onSelect)
    {
        var nav = new StackPanel();
        Grid.SetColumn(nav, 0);
        categoryList.Children.Add(nav);

        var keyListPanel = new StackPanel();
        var keyScroll = new ScrollViewer
        {
            Content = keyListPanel,
            // Hidden, not Auto — the mouse wheel and the edge auto-scroll
            // below still work either way; this just drops the native
            // scrollbar track/thumb, which read as an out-of-place plain
            // white/gray element against this theme (Fizzil's own
            // feedback).
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
                    Focusable = false, // no focus-rectangle blip on the first item
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

    // The animated-expand/collapse upgrade Fizzil asked for: grows/shrinks
    // an element's Height instead of snapping straight to its final size
    // the way the WinForms version's accordion-via-resize did. Shared by
    // every category list and the timing panel below — a plain
    // FrameworkElement, not specifically a StackPanel, since the timing
    // panel is a Grid.
    private void AnimateHeight(FrameworkElement element, bool expand)
    {
        if (expand)
        {
            element.Visibility = Visibility.Visible;
            element.Height = double.NaN;
            // Before the first layout pass ActualWidth is 0 and Width is
            // unset (NaN) now that the card stretches to its host, and a
            // NaN measure throws — unconstrained is the safe fallback.
            element.Measure(new Size(ActualWidth > 0 ? ActualWidth : double.PositiveInfinity, double.PositiveInfinity));
            double targetHeight = element.DesiredSize.Height;
            element.Height = 0;

            var anim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            // Clears the explicit Height once the animation lands, so the
            // element still sizes naturally to itself afterward instead
            // of staying pinned to whatever height it happened to measure
            // at the moment it opened.
            anim.Completed += (_, _) => element.ClearValue(HeightProperty);
            element.BeginAnimation(HeightProperty, anim);
        }
        else
        {
            double currentHeight = element.ActualHeight;
            var anim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) => element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(HeightProperty, anim);
        }
    }

    // Same animation, but to a caller-given height instead of measuring
    // the element's own natural size — used for the category split view,
    // which is a fixed height by design (see AddKeyRow's own comment),
    // not something that should grow to fit however many keys the
    // longest category has.
    private void AnimateHeight(FrameworkElement element, bool expand, double explicitTargetHeight)
    {
        if (expand)
        {
            element.Visibility = Visibility.Visible;
            element.Height = 0;
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
            anim.Completed += (_, _) => element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(HeightProperty, anim);
        }
    }

    private void UpdateDurationText() => DurationText.Text = $"{_duration.ToString("0.0", CultureInfo.InvariantCulture)}s";

    private void SaveBehavior() =>
        _source.SetBehavior(_id, _repeatOn, _holdOn, _duration, _infiniteOn,
            _useCustomRepeatIntervals, _keyIntervalSeconds);

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _repeatOn = !_repeatOn;
        if (_repeatOn)
        {
            _holdOn = false;
            HoldButton.Tag = false;
        }
        RepeatButton.Tag = _repeatOn;
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        UpdateRepeatIntervalVisibility(animate: true);
        SaveBehavior();
    }

    private void HoldButton_Click(object sender, RoutedEventArgs e)
    {
        _holdOn = !_holdOn;
        if (_holdOn)
        {
            _repeatOn = false;
            RepeatButton.Tag = false;
        }
        HoldButton.Tag = _holdOn;
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        UpdateRepeatIntervalVisibility(animate: true);
        SaveBehavior();
    }

    // +1/+0.1/reset target whichever K is selected in the repeat interval
    // row instead of the main duration — editing a key's gap isn't
    // "moving away from Infinite" the way editing the main duration is,
    // so Infinite is left alone for it. Matches RemapCardTab's own
    // plusOne/plusTenth/resetDurationButton handlers (WinForms).
    private void PlusOneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKIndex >= 0)
        {
            _keyIntervalSeconds[_selectedKIndex] = Math.Round(_keyIntervalSeconds[_selectedKIndex] + 1.0, 1);
            RebuildKeyIntervalRow();
            SaveBehavior();
            return;
        }
        _duration = Math.Round(_duration + 1.0, 1);
        _infiniteOn = false;
        InfiniteButton.Tag = false;
        UpdateDurationText();
        SaveBehavior();
    }

    private void PlusTenthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKIndex >= 0)
        {
            _keyIntervalSeconds[_selectedKIndex] = Math.Round(_keyIntervalSeconds[_selectedKIndex] + 0.1, 1);
            RebuildKeyIntervalRow();
            SaveBehavior();
            return;
        }
        _duration = Math.Round(_duration + 0.1, 1);
        _infiniteOn = false;
        InfiniteButton.Tag = false;
        UpdateDurationText();
        SaveBehavior();
    }

    // Reset targets exactly whichever one K is selected, same as
    // +1/+0.1 — just that key's own gap. With the row visible but no
    // particular K selected, it resets every key's gap at once instead.
    // Otherwise it's just the main duration.
    private void ResetDurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKIndex >= 0)
        {
            _keyIntervalSeconds[_selectedKIndex] = 0.0;
            RebuildKeyIntervalRow();
            SaveBehavior();
            return;
        }
        if (_useCustomRepeatIntervals)
        {
            for (int i = 0; i < _keyIntervalSeconds.Count; i++)
                _keyIntervalSeconds[i] = 0.0;
            RebuildKeyIntervalRow();
            SaveBehavior();
            return;
        }
        _duration = 0.0;
        UpdateDurationText();
        SaveBehavior();
    }

    private void RepeatIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        _useCustomRepeatIntervals = !_useCustomRepeatIntervals;
        RepeatIntervalButton.Tag = _useCustomRepeatIntervals;
        if (!_useCustomRepeatIntervals)
            _selectedKIndex = -1;
        SaveBehavior();
        UpdateRepeatIntervalVisibility(animate: true);
    }

    // Rebuilt whenever a key is added/removed (the K count changes) or a
    // different K is selected (to refresh which one is lit up) — mirrors
    // RemapCardTab's own RebuildKeyIntervalRow (WinForms): one K button
    // plus its gap readout per key, laid out as column pairs.
    private void RebuildKeyIntervalRow()
    {
        KeyIntervalRow.Children.Clear();
        KeyIntervalRow.ColumnDefinitions.Clear();

        for (int idx = 0; idx < _keyIntervalSeconds.Count; idx++)
        {
            int kIndex = idx; // captured per-button, not the loop variable
            KeyIntervalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            KeyIntervalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var kButton = new Button
            {
                Content = $"K{kIndex + 1}",
                Tag = _selectedKIndex == kIndex,
                Style = (System.Windows.Style)FindResource("TinyButtonStyle"),
            };
            kButton.Click += (_, _) =>
            {
                _selectedKIndex = _selectedKIndex == kIndex ? -1 : kIndex;
                RebuildKeyIntervalRow();
            };
            Grid.SetColumn(kButton, idx * 2);
            KeyIntervalRow.Children.Add(kButton);

            var kLabel = new TextBlock
            {
                Text = $"{_keyIntervalSeconds[kIndex].ToString("0.0", CultureInfo.InvariantCulture)}s",
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            kLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(kLabel, idx * 2 + 1);
            KeyIntervalRow.Children.Add(kLabel);
        }
    }

    // Recomputes whether Repeat Interval can even apply (Repeat is on and
    // there are 2+ keys), turning it off if the eligibility just
    // disappeared — same guard as RemapCardTab's own canCustomizeRepeatInterval
    // check (WinForms) — then shows/hides the button and, if it's on, the
    // K-row beneath it.
    private void UpdateRepeatIntervalVisibility(bool animate)
    {
        bool canCustomize = _repeatOn && _source.ExtraWords[_id].Count >= 1;
        if (!canCustomize && _useCustomRepeatIntervals)
        {
            _useCustomRepeatIntervals = false;
            RepeatIntervalButton.Tag = false;
        }
        if (!canCustomize)
            _selectedKIndex = -1;

        SetElementVisible(RepeatIntervalButton, canCustomize, animate);
        SetElementVisible(KeyIntervalRow, canCustomize && _useCustomRepeatIntervals, animate);
    }

    private void SetElementVisible(FrameworkElement element, bool visible, bool animate)
    {
        if (animate)
        {
            AnimateHeight(element, visible);
        }
        else if (visible)
        {
            element.Visibility = Visibility.Visible;
            element.ClearValue(HeightProperty);
        }
        else
        {
            element.Visibility = Visibility.Collapsed;
        }
    }

    // Infinite ignores the duration entirely, so turning it on clears
    // whatever value was set — otherwise it'd look like a leftover
    // duration that doesn't actually do anything anymore.
    private void InfiniteButton_Click(object sender, RoutedEventArgs e)
    {
        _infiniteOn = !_infiniteOn;
        InfiniteButton.Tag = _infiniteOn;
        if (_infiniteOn)
        {
            _duration = 0.0;
            UpdateDurationText();
        }
        SaveBehavior();
    }

    // Resets everything about this card: the assigned key(s), Repeat/
    // Hold/Infinite, the duration, and every key's repeat-interval gap.
    // Matches RemapCardTab's own ResetCard local function (WinForms).
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
        _selectedKIndex = -1;
        _useCustomRepeatIntervals = false;

        RepeatButton.Tag = _repeatOn;
        HoldButton.Tag = _holdOn;
        InfiniteButton.Tag = _infiniteOn;
        RepeatIntervalButton.Tag = false;
        UpdateDurationText();
        SetElementVisible(TimingPanel, _repeatOn || _holdOn, animate: true);
        RebuildKeyIntervalRow();
        UpdateRepeatIntervalVisibility(animate: true);
        SetElementVisible(ResetAllButton, false, animate: true);
    }

    // Easter egg: 3 taps within 600ms expands a "Reset All" row underneath
    // (an accordion, same as everything else here) with a button that
    // resets every card, not just this one — every tap resets THIS card
    // immediately regardless, same as RemapCardTab's own resetCardButton
    // handler (WinForms): tapping Reset 3 times fast just resets an
    // already-reset card 3 times, and also reveals Reset All on the third.
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _resetTapCount++;
        _resetTapTimer.Stop();
        _resetTapTimer.Start();

        ResetCard();

        if (_resetTapCount == 3)
        {
            _resetTapCount = 0;
            SetElementVisible(ResetAllButton, true, animate: true);
        }
    }

    // This card resets itself; the host (DashboardShell) resets every
    // other mapping in response to the event and has a visible Reset All
    // in Settings too — the triple-tap here is a shortcut, not the only
    // route.
    private void ResetAllButton_Click(object sender, RoutedEventArgs e)
    {
        ResetCard();
        ResetAllRequested?.Invoke();
    }
}
