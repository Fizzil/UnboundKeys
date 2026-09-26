using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static UnboundKeys.KeyboardLayout;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// The Keyboard page's map, made to type: the same rows (KeyboardLayout),
// the same row height and key look, so it reads as the same keyboard
// (Fizzil's ask). Letters and digits are the lit key caps; the outlined
// keys that mean something for a name are live too — Space, Backspace,
// Shift, Caps, the punctuation, Enter for Done, Esc for Cancel, Del to
// clear — and the rest (Tab, Ctrl, Win, Alt, the arrows) stay as quiet
// outlines. Shift is one letter's worth and lights up at each word
// start, so names come out capitalized on their own; Caps locks.
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
        _shift = AtWordStart();
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
                return BuildOutlinedKey("", Space);
            case "⌫":
                return BuildOutlinedKey(spec.Label, Backspace);
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
                return BuildOutlinedKey(spec.Label, () => TypePunctuation(spec));
        }
    }

    // A letter or digit, in the on-screen keyboard's lit key cap.
    private Button BuildTypingKey(KeySpec spec)
    {
        char c = spec.Label[0];
        var label = new TextBlock
        {
            Text = spec.Label,
            FontSize = 13,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (char.IsLetter(c))
            _letters.Add((label, char.ToLowerInvariant(c)));

        var key = new Button { Content = label };
        key.SetResourceReference(StyleProperty, "KeyCapStyle");
        key.Click += (_, _) => Type(char.ToLowerInvariant(c));
        return key;
    }

    private static Button BuildOutlinedKey(string label, Action onClick)
    {
        var key = BuildOutlinedKey(label, out _, out _);
        key.Click += (_, _) => onClick();
        return key;
    }

    // The Keyboard page's outlined key, as a button: the outline sits on
    // a ghost button that fills on hover.
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
        if (char.IsLetter(c))
        {
            _text += _shift ^ _caps ? char.ToUpperInvariant(c) : c;
            ReleaseShift();
        }
        else
        {
            _text += c;
        }
        UpdatePreview();
    }

    private void TypePunctuation(KeySpec spec)
    {
        if (_text.Length >= MaxLength)
            return;
        string glyph = _shift && spec.ShiftLabel is { Length: 1 } shifted ? shifted : spec.Label;
        _text += glyph;
        ReleaseShift();
        UpdatePreview();
    }

    private void Space()
    {
        if (_text.Length >= MaxLength || AtWordStart())
            return;
        _text += ' ';
        _shift = true;
        RefreshCase();
        UpdatePreview();
    }

    private void Backspace()
    {
        if (_text.Length == 0)
            return;
        _text = _text[..^1];
        _shift = AtWordStart();
        RefreshCase();
        UpdatePreview();
    }

    private void Clear()
    {
        _text = "";
        _shift = true;
        RefreshCase();
        UpdatePreview();
    }

    private void Finish()
    {
        if (DoneButton.IsEnabled)
            Done?.Invoke(_text.Trim());
    }

    // Shift is one keystroke's worth.
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
        text.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextPrimaryBrush" : "TextSecondaryBrush");
        text.Opacity = on ? 1.0 : 0.6;
    }

    private void UpdatePreview()
    {
        PreviewText.Text = _text;
        PlaceholderText.Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        string trimmed = _text.Trim();
        DoneButton.IsEnabled = trimmed.Length > 0 && (_isAcceptable?.Invoke(trimmed) ?? true);
    }
}
