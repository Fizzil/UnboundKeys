using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// A section that folds under its heading: the title, a one-line summary
// of its state while closed, and a chevron at the far right; the body is
// this control's Content. Settings uses one per section and the Help
// page one per topic (Fizzil: the pages were getting long, and a closed
// row that still says "Red" or "5 profiles, Default active" loses
// nothing). Looks come from the FoldSection style in Theme.Controls.xaml.
public class FoldSection : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(FoldSection), new PropertyMetadata(""));

    public static readonly DependencyProperty SummaryProperty =
        DependencyProperty.Register(nameof(Summary), typeof(string), typeof(FoldSection), new PropertyMetadata(""));

    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(FoldSection), new PropertyMetadata(false));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public FoldSection()
    {
        Focusable = false;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Header") is Button header)
            header.Click += (_, _) => IsOpen = !IsOpen;
    }
}
