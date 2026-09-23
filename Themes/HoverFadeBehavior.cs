using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace UnboundKeys.Themes;

// FlatButtonBase's hover/press color fade can't be a plain declarative
// Storyboard inside the ControlTemplate's Triggers — WPF freezes/seals a
// Style's ControlTemplate once for reuse across every control that uses
// it, and that seal fails outright if any animated value is a
// DynamicResource (needed here, so a hover fade always targets whichever
// color theme is CURRENTLY active rather than whatever was active the
// first time the template got parsed). Confirmed by testing: it throws
// "Cannot freeze this Storyboard timeline tree for use across threads" as
// soon as a second button using the same style triggers the seal.
//
// Doing the fade in code sidesteps that entirely: a fresh ColorAnimation,
// resolving colors via FindResource at the moment each event actually
// fires, applied directly to the template's named background brush —
// nothing here ever needs to be frozen for sharing.
internal static class HoverFadeBehavior
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(HoverFadeBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button || e.NewValue is not true)
            return;

        // The template's named "BgBrush" element doesn't exist yet at
        // Style-apply time — Loaded is the first point the template's
        // visual tree is actually built and FindName can see into it.
        button.Loaded += (_, _) => Wire(button);
    }

    private static void Wire(Button button)
    {
        if (button.Template.FindName("BgBrush", button) is not SolidColorBrush brush)
            return;

        var hoverDuration = (Duration)button.FindResource("HoverFadeDuration");
        var pressDuration = (Duration)button.FindResource("PressFadeDuration");

        void FadeTo(string colorResourceKey, Duration duration)
        {
            var color = (Color)button.FindResource(colorResourceKey);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, duration));
        }

        button.MouseEnter += (_, _) =>
        {
            if (!button.IsPressed)
                FadeTo("HoverColor", hoverDuration);
        };
        button.MouseLeave += (_, _) => FadeTo("ButtonColor", hoverDuration);
        button.PreviewMouseLeftButtonDown += (_, _) => FadeTo("AccentColor", pressDuration);
        button.PreviewMouseLeftButtonUp += (_, _) =>
            FadeTo(button.IsMouseOver ? "HoverColor" : "ButtonColor", pressDuration);
    }
}
