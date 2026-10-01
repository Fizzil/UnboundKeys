using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static UnboundKeys.KeyboardLayout;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// The Keyboard page's map, made to type: the same rows (KeyboardLayout),
// the same row height and key look, so it reads as the same keyboard
// (Fizzil). Every key is the same outline, letters and digits included; the
// keys that mean something for a name are live too — Space, Backspace,
// Shift, Caps, the punctuation, Enter for Done, Esc for Cancel, Del to
// clear — and the rest (Tab, Ctrl, Win, Alt, the arrows) stay as quiet
// outlines. Shift is sticky, as on the on-screen keyboard: click it and
// it stays lit for the next key typed (a held key keeps it for its whole
// run), then lets go; Caps locks. Nothing is capitalized on its own
// (Fizzil: Shift is there for whoever wants a capital).
public partial class NameKeyboard
{
    private const int MaxLength = 20;

    private string _text = "";
    private bool _shift;
    private bool _caps;
    private Func<string, bool>? _isAcceptable;

    private readonly List<(TextBlock Label, char Letter)> _letters = new();
    private readonly List<(Border Outline, TextBlock Text)> _shiftKeys = new();
    private readonly List<(Border Outline, TextBlock Text)> _capsKeys = new();

    public event Action<string>? Done;
    public event Action? Cancelled;

    public NameKeyboard()
    {
        InitializeComponent();

        foreach (var row in FullRows())
            Rows.Children.Add(BuildRow(row));

        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        DoneButton.Click += (_, _) => Finish();
        UpdatePreview();
    }

    // Starts an edit: the current name (or "" for a new one) and the
    // caller's rule for whether a typed name may be accepted — used to
    // keep Done disabled while the name is empty or already taken.
    public void Begin(string initialText, Func<string, bool> isAcceptable)
    {
        _text = initialText;
        _isAcceptable = isAcceptable;
        _caps = false;
        _shift = false;
        RefreshCase();
        UpdatePreview();
    }

    private Grid BuildRow(KeySpec[] specs)
    {
        var row = new Grid { Height = 36 };
        foreach (var spec in specs)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spec.Width, GridUnitType.Star) });

        for (int i = 0; i < specs.Length; i++)
        {
            var key = BuildKey(specs[i]);
            Grid.SetColumn(key, i);
            row.Children.Add(key);
        }
        return row;
    }

    private FrameworkElement BuildKey(KeySpec spec)
    {
        if (spec.Kind == KeyKind.Remappable)
            return BuildTypingKey(spec);

        if (spec.Kind == KeyKind.StickyFixed && spec.Label == "Shift")
        {
            var shift = BuildOutlinedKey(spec.Label, out var outline, out var text);
            _shiftKeys.Add((outline, text));
            shift.Click += (_, _) =>
            {
                _shift = !_shift;
                RefreshCase();
            };
            return shift;
        }

        if (spec.Kind != KeyKind.Plain)
            return BuildInertKey(spec);

        switch (spec.Label)
        {
            case "":
                return BuildRepeatingKey("", Space);
            case "⌫":
                return BuildRepeatingKey(spec.Label, Backspace);
            case "Del":
                return BuildOutlinedKey(spec.Label, Clear);
            case "Enter":
                return BuildOutlinedKey(spec.Label, Finish);
            case "Esc":
                return BuildOutlinedKey(spec.Label, () => Cancelled?.Invoke());
            case "Caps":
            {
                var caps = BuildOutlinedKey(spec.Label, out var outline, out var text);
                _capsKeys.Add((outline, text));
                caps.Click += (_, _) =>
                {
                    _caps = !_caps;
                    RefreshCase();
                };
                return caps;
            }
            case "Tab":
            case "←":
            case "↓":
            case "↑":
            case "→":
                return BuildInertKey(spec);
            default:
                // Punctuation: the shifted glyph while Shift is lit.
                return BuildRepeatingKey(spec.Label, () => TypePunctuation(spec), ReleaseShift);
        }
    }

    // A letter or digit: the same outline as every other key here. The lit
    // caps on the Keyboard page mean "remappable", which is not the point
    // of this keyboard (Fizzil).
    private Button BuildTypingKey(KeySpec spec)
    {
        char c = char.ToLowerInvariant(spec.Label[0]);
        var key = BuildOutlinedKey(spec.Label.ToLowerInvariant(), out _, out var label);
        if (char.IsLetter(c))
            _letters.Add((label, c));
        WireRepeat(key, () => Type(c), ReleaseShift);
        return key;
    }

    private static Button BuildOutlinedKey(string label, Action onClick)
    {
        var key = BuildOutlinedKey(label, out _, out _);
        key.Click += (_, _) => onClick();
        return key;
    }

    private static Button BuildRepeatingKey(string label, Action onPress, Action? onRelease = null)
    {
        var key = BuildOutlinedKey(label, out _, out _);
        WireRepeat(key, onPress, onRelease);
        return key;
    }

    // The keys that type (letters, digits, punctuation, Space, Backspace)
    // act like the on-screen keyboard's (Fizzil): once on the way down,
    // then again and again while held, on the same schedule
    // (KeyExecutor.HoldRepeatDelayMs, then RepeatIntervalMs), so holding
    // Backspace clears a name quickly and no typing key feels dead when
    // held. Shift, Caps, Del, Enter and Esc switch or finish something, so
    // they stay single clicks. Preview events rather than Click, which
    // only fires on release. The button captures the mouse while pressed,
    // so a release anywhere still reaches it; losing capture some other
    // way must end the repeat too. onRelease runs once when the press
    // ends: the keys that use Shift let it go there, not per press, so a
    // held key keeps Shift for its whole run, as on the on-screen keyboard.
    private static void WireRepeat(Button key, Action onPress, Action? onRelease = null)
    {
        System.Windows.Threading.DispatcherTimer? timer = null;
        bool pressed = false;

        void Release()
        {
            if (!pressed)
                return;
            pressed = false;
            timer?.Stop();
            timer = null;
            onRelease?.Invoke();
        }

        key.PreviewMouseLeftButtonDown += (_, _) =>
        {
            Release();
            pressed = true;
            onPress();
            var repeat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(KeyExecutor.HoldRepeatDelayMs) };
            repeat.Tick += (_, _) =>
            {
                repeat.Interval = TimeSpan.FromMilliseconds(KeyExecutor.RepeatIntervalMs);
                onPress();
            };
            timer = repeat;
            repeat.Start();
        };
        key.PreviewMouseLeftButtonUp += (_, _) => Release();
        key.LostMouseCapture += (_, _) => Release();
    }

    // The Keyboard page's outlined key, as a button: the outline sits on
    // a ghost button that fills on hover, and clicks like the on-screen
    // keyboard's keys do (KeyClick).
    private static Button BuildOutlinedKey(string label, out Border outline, out TextBlock text)
    {
        text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.6,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        outline = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
        };
        outline.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");

        var key = new Button
        {
            Content = outline,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        key.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        key.PreviewMouseLeftButtonDown += (_, _) => KeyClick.Play();
        // The key under the pointer shows its character in white (Fizzil:
        // to see where the mouse is on a keyboard of quiet outlines); off
        // it, back to quiet, unless it is a lit Shift or Caps (see Light).
        var keyText = text; // an out parameter cannot be captured
        key.MouseEnter += (_, _) => Brighten(keyText, true);
        key.MouseLeave += (_, _) => Brighten(keyText, keyText.Tag is true);
        return key;
    }

    // Exactly the Keyboard page's non-interactive outline.
    private static Border BuildInertKey(KeySpec spec)
    {
        var text = new TextBlock
        {
            Text = spec.Label,
            FontSize = 11,
            Opacity = 0.6,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var outline = new Border
        {
            Child = text,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        outline.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return outline;
    }

    private void Type(char c)
    {
        if (_text.Length >= MaxLength)
            return;
        _text += char.IsLetter(c) && (_shift ^ _caps) ? char.ToUpperInvariant(c) : c;
        UpdatePreview();
    }

    private void TypePunctuation(KeySpec spec)
    {
        if (_text.Length >= MaxLength)
            return;
        string glyph = _shift && spec.ShiftLabel is { Length: 1 } shifted ? shifted : spec.Label;
        _text += glyph;
        UpdatePreview();
    }

    private void Space()
    {
        if (_text.Length >= MaxLength || AtWordStart())
            return;
        _text += ' ';
        UpdatePreview();
    }

    private void Backspace()
    {
        if (_text.Length == 0)
            return;
        _text = _text[..^1];
        UpdatePreview();
    }

    private void Clear()
    {
        _text = "";
        UpdatePreview();
    }

    private void Finish()
    {
        if (DoneButton.IsEnabled)
            Done?.Invoke(_text.Trim());
    }

    // Shift lets go once the key that used it is released (see WireRepeat).
    private void ReleaseShift()
    {
        if (!_shift)
            return;
        _shift = false;
        RefreshCase();
    }

    private bool AtWordStart() => _text.Length == 0 || _text[^1] == ' ';

    // Letter caps follow the case they would type; Shift and Caps light
    // up (accent outline, bright text) while they are on.
    private void RefreshCase()
    {
        bool upper = _shift ^ _caps;
        foreach (var (label, letter) in _letters)
            label.Text = (upper ? char.ToUpperInvariant(letter) : letter).ToString();
        foreach (var (outline, text) in _shiftKeys)
            Light(outline, text, _shift);
        foreach (var (outline, text) in _capsKeys)
            Light(outline, text, _caps);
    }

    private static void Light(Border outline, TextBlock text, bool on)
    {
        outline.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "CardBorderBrush");
        text.Tag = on; // what the key goes back to when the pointer leaves it
        Brighten(text, on || (outline.Parent as Button)?.IsMouseOver == true);
    }

    // A key's character: white and solid, or the quiet grey of the map.
    private static void Brighten(TextBlock text, bool bright)
    {
        text.SetResourceReference(TextBlock.ForegroundProperty, bright ? "TextPrimaryBrush" : "TextSecondaryBrush");
        text.Opacity = bright ? 1.0 : 0.6;
    }

    private void UpdatePreview()
    {
        PreviewText.Text = _text;
        PlaceholderText.Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        string trimmed = _text.Trim();
        DoneButton.IsEnabled = trimmed.Length > 0 && (_isAcceptable?.Invoke(trimmed) ?? true);
    }
}
