using System;
using System.Windows;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Size = System.Windows.Size;

namespace UnboundKeys.Wpf;

// First real slice of RemapCardTab.cs (WinForms) ported to WPF — the Key
// row and the merged Repeat/Hold/Reset row, inside the new rounded/
// glowing card border. Deliberately NOT the whole card yet: extra keys,
// the timing row, Repeat Interval, and the Reset All easter egg are still
// to come — this proves out the animated-accordion pattern and the fancy
// design system against one real, working slice first, wired to actual
// IRemapSource data rather than fake preview content.
public partial class RemapCard
{
    private readonly IRemapSource _source;
    private readonly string _id;
    private bool _keyExpanded;
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

        SetKeyRowText(_source.KeyLabelFor(_id));
        BuildCategoryList();
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

    private void SetKeyRowText(string fullLabel)
    {
        var (label, value) = SplitKeyLabel(fullLabel);
        KeyLabelText.Text = label;
        KeyValueButton.Content = value;
    }

    // One button per KeyCatalog category (Letters, Numbers, ...) — hovering
    // one opens CategoryKeyPopup (Phase 4) with that category's individual
    // keys, exactly like RemapCardTab's own BuildCategoryButtons does.
    private void BuildCategoryList()
    {
        CategoryList.Children.Clear();
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
                _openCategoryPopup = CategoryKeyPopup.Show(categoryButton, Width, keys, entry =>
                {
                    _source.Rebind(_id, entry.VkCode);
                    SetKeyRowText(_source.KeyLabelFor(_id));
                });
            };
            CategoryList.Children.Add(categoryButton);
        }
    }

    private void KeyValueButton_Click(object sender, RoutedEventArgs e)
    {
        _keyExpanded = !_keyExpanded;
        // Shares FlatButtonBase's underline trigger — reads as "selected"
        // the same way a toggle button does, even though this particular
        // button is muted-colored rather than full accent.
        KeyValueButton.Tag = _keyExpanded;
        ToggleCategoryList(_keyExpanded);
    }

    // The animated-expand/collapse upgrade Fizzil asked for: grows/shrinks
    // the category list's Height instead of snapping straight to its
    // final size the way the WinForms version's accordion-via-resize did.
    private void ToggleCategoryList(bool expanded)
    {
        if (expanded)
        {
            CategoryList.Visibility = Visibility.Visible;
            CategoryList.Height = double.NaN;
            CategoryList.Measure(new Size(ActualWidth > 0 ? ActualWidth : Width, double.PositiveInfinity));
            double targetHeight = CategoryList.DesiredSize.Height;
            CategoryList.Height = 0;

            var anim = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            // Clears the explicit Height once the animation lands, so the
            // list still sizes naturally to itself afterward instead of
            // staying pinned to whatever height it happened to measure at
            // the moment it opened.
            anim.Completed += (_, _) => CategoryList.ClearValue(HeightProperty);
            CategoryList.BeginAnimation(HeightProperty, anim);
        }
        else
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;

            double currentHeight = CategoryList.ActualHeight;
            var anim = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            anim.Completed += (_, _) => CategoryList.Visibility = Visibility.Collapsed;
            CategoryList.BeginAnimation(HeightProperty, anim);
        }
    }

    // Repeat/Hold aren't wired to real persistence yet — SetBehavior needs
    // a duration/infinite value, which comes from the timing row (not
    // part of this first slice) — so for now this is just the visual
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
        SetKeyRowText(_source.KeyLabelFor(_id));
        _repeatOn = false;
        _holdOn = false;
        RepeatButton.Tag = false;
        HoldButton.Tag = false;
    }
}
