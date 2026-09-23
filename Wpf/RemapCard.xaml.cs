using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace UnboundKeys.Wpf;

// RemapCardTab.cs (WinForms) ported to WPF, one increment at a time —
// this slice adds the timing row (duration, +1/+0.1/reset, Infinite) and
// wires Repeat/Hold up to real persistence. Still to come: Repeat
// Interval and the Reset All easter egg.
public partial class RemapCard
{
    private readonly IRemapSource _source;
    private readonly string _id;
    private bool _repeatOn;
    private bool _holdOn;
    private double _duration;
    private bool _infiniteOn;
    private Window? _openCategoryPopup;

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

        RebuildKeyGroup();
        RepeatButton.Tag = _repeatOn;
        HoldButton.Tag = _holdOn;
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetTimingPanelVisible(_repeatOn || _holdOn, animate: false);
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
        _openCategoryPopup?.Close();
        _openCategoryPopup = null;
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
                RebuildKeyGroup();
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
                RebuildKeyGroup();
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

        var labelText = new TextBlock { Text = label, Style = (System.Windows.Style)FindResource("LabelDisplayStyle") };
        Grid.SetColumn(labelText, 0);
        grid.Children.Add(labelText);

        var valueButton = new Button
        {
            Content = value,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Right,
            Style = (System.Windows.Style)FindResource("ValueButtonStyle"),
        };
        Grid.SetColumn(valueButton, 1);
        grid.Children.Add(valueButton);

        var categoryList = new StackPanel { Visibility = Visibility.Collapsed, ClipToBounds = true };

        bool expanded = false;
        valueButton.Click += (_, _) =>
        {
            expanded = !expanded;
            valueButton.Tag = expanded;
            if (!expanded)
            {
                _openCategoryPopup?.Close();
                _openCategoryPopup = null;
            }
            AnimateHeight(categoryList, expanded);
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

    // One button per KeyCatalog category (Letters, Numbers, ...) — hovering
    // one opens CategoryKeyPopup (Phase 4) with that category's individual
    // keys, exactly like RemapCardTab's own BuildCategoryButtons does.
    private void PopulateCategoryList(StackPanel categoryList, Action<KeyCatalog.Entry> onSelect)
    {
        foreach (var (category, keys) in KeyCatalog.Groups)
        {
            var categoryButton = new Button
            {
                Content = category,
                Height = 40,
                Style = (System.Windows.Style)FindResource("NestedButtonStyle"),
            };
            categoryButton.MouseEnter += (_, _) =>
            {
                _openCategoryPopup?.Close();
                _openCategoryPopup = CategoryKeyPopup.Show(categoryButton, Width, keys, onSelect);
            };
            categoryList.Children.Add(categoryButton);
        }
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
            element.Measure(new Size(ActualWidth > 0 ? ActualWidth : Width, double.PositiveInfinity));
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

    private void SetTimingPanelVisible(bool visible, bool animate)
    {
        if (animate)
        {
            AnimateHeight(TimingPanel, visible);
        }
        else if (visible)
        {
            TimingPanel.Visibility = Visibility.Visible;
            TimingPanel.ClearValue(HeightProperty);
        }
        else
        {
            TimingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateDurationText() => DurationText.Text = $"{_duration:0.0}s";

    // Preserves whatever Repeat Interval settings are already saved
    // (UseCustomRepeatIntervals/RepeatKeyIntervalsSeconds) rather than
    // overwriting them — that row isn't part of this slice yet, so
    // there's nothing here to read them FROM except what's already on
    // disk.
    private void SaveBehavior()
    {
        var existing = _source.Behaviors[_id];
        _source.SetBehavior(_id, _repeatOn, _holdOn, _duration, _infiniteOn,
            existing.UseCustomRepeatIntervals, existing.RepeatKeyIntervalsSeconds);
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _repeatOn = !_repeatOn;
        if (_repeatOn)
        {
            _holdOn = false;
            HoldButton.Tag = false;
        }
        RepeatButton.Tag = _repeatOn;
        SetTimingPanelVisible(_repeatOn || _holdOn, animate: true);
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
        SetTimingPanelVisible(_repeatOn || _holdOn, animate: true);
        SaveBehavior();
    }

    // +1/+0.1 disengage Infinite the same way RemapCardTab's own
    // DisengageInfinite does — adjusting a concrete duration only makes
    // sense once Infinite (which ignores duration entirely) is off.
    private void PlusOneButton_Click(object sender, RoutedEventArgs e)
    {
        _duration = Math.Round(_duration + 1.0, 1);
        _infiniteOn = false;
        InfiniteButton.Tag = false;
        UpdateDurationText();
        SaveBehavior();
    }

    private void PlusTenthButton_Click(object sender, RoutedEventArgs e)
    {
        _duration = Math.Round(_duration + 0.1, 1);
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

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _source.ResetToDefault(_id);
        RebuildKeyGroup();

        var behavior = _source.Behaviors[_id];
        _repeatOn = behavior.Repeat;
        _holdOn = behavior.Hold;
        _duration = behavior.DurationSeconds;
        _infiniteOn = behavior.Infinite;
        RepeatButton.Tag = _repeatOn;
        HoldButton.Tag = _holdOn;
        InfiniteButton.Tag = _infiniteOn;
        UpdateDurationText();
        SetTimingPanelVisible(_repeatOn || _holdOn, animate: true);
    }
}
