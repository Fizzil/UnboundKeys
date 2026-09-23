using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace UnboundKeys.Wpf;

// RemapCardTab.cs (WinForms) ported to WPF, one increment at a time —
// this slice adds Key 1, "Add Key", and up to two extra keys (each with
// its own always-visible, two-tap confirm delete "✕"), plus the merged
// Repeat/Hold/Reset row. Still to come: the timing row, Repeat Interval,
// and the Reset All easter egg.
public partial class RemapCard
{
    private readonly IRemapSource _source;
    private readonly string _id;
    private bool _repeatOn;
    private bool _holdOn;
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

        RebuildKeyGroup();
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
            ToggleList(categoryList, expanded);
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
    // a list's Height instead of snapping straight to its final size the
    // way the WinForms version's accordion-via-resize did.
    private void ToggleList(StackPanel list, bool expanded)
    {
        if (expanded)
        {
            list.Visibility = Visibility.Visible;
            list.Height = double.NaN;
            list.Measure(new Size(ActualWidth > 0 ? ActualWidth : Width, double.PositiveInfinity));
            double targetHeight = list.DesiredSize.Height;
            list.Height = 0;

            var anim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            // Clears the explicit Height once the animation lands, so the
            // list still sizes naturally to itself afterward instead of
            // staying pinned to whatever height it happened to measure at
            // the moment it opened.
            anim.Completed += (_, _) => list.ClearValue(HeightProperty);
            list.BeginAnimation(HeightProperty, anim);
        }
        else
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;

            double currentHeight = list.ActualHeight;
            var anim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) => list.Visibility = Visibility.Collapsed;
            list.BeginAnimation(HeightProperty, anim);
        }
    }

    // Repeat/Hold aren't wired to real persistence yet — SetBehavior needs
    // a duration/infinite value, which comes from the timing row (not
    // part of this slice yet) — so for now this is just the visual
    // toggle + mutual-exclusivity behavior, same underline treatment the
    // real thing will use once the timing row exists.
    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _repeatOn = !_repeatOn;
        if (_repeatOn)
        {
            _holdOn = false;
            HoldButton.Tag = false;
        }
        RepeatButton.Tag = _repeatOn;
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
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _source.ResetToDefault(_id);
        RebuildKeyGroup();
        _repeatOn = false;
        _holdOn = false;
        RepeatButton.Tag = false;
        HoldButton.Tag = false;
    }
}
