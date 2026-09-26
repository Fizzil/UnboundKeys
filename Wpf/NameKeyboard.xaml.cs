using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// A QWERTY keyboard for typing a name with the mouse: digits, letters,
// dash and apostrophe, Space, Backspace, and Shift/Caps for capitals. The
// first letter of each word comes out capitalized on its own (Shift lights
// up at a word start and can be clicked off); Caps locks capitals. Same
// key caps as the on-screen keyboard, so it reads as the same thing.
public partial class NameKeyboard
{
    private const int MaxLength = 20;

    // Rows as "label:units" specs, every row 12 units wide so the keys
    // line up in columns like a real keyboard. "_" is an empty gap; a
    // one-character label types itself; the named keys act by name.
    private static readonly string[][] RowSpecs =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "⌫:2" },
        new[] { "_:0.5", "q", "w", "e", "r", "t", "y", "u", "i", "o", "p", "_:1.5" },
        new[] { "Caps", "a", "s", "d", "f", "g", "h", "j", "k", "l", "'", "-" },
        new[] { "Shift:1.5", "z", "x", "c", "v", "b", "n", "m", ",", ".", "_:1.5" },
        new[] { "_:3", "Space:6", "_:3" },
    };

    private string _text = "";
    private bool _shift;
    private bool _caps;
    private Func<string, bool>? _isAcceptable;
    private readonly List<(Button Key, char Letter)> _letters = new();
    private Button? _shiftKey;
    private Button? _capsKey;

    public event Action<string>? Done;
    public event Action? Cancelled;

    public NameKeyboard()
    {
        InitializeComponent();

        foreach (var specs in RowSpecs)
            Rows.Children.Add(BuildRow(specs));

        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        DoneButton.Click += (_, _) => Done?.Invoke(_text.Trim());
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

    private Grid BuildRow(string[] specs)
    {
        var row = new Grid { Height = 48 };
        for (int i = 0; i < specs.Length; i++)
        {
            var (label, units) = Parse(specs[i]);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(units, GridUnitType.Star) });
            if (label == "_")
                continue;
            var key = BuildKey(label);
            Grid.SetColumn(key, i);
            row.Children.Add(key);
        }
        return row;
    }

    private static (string Label, double Units) Parse(string spec)
    {
        int colon = spec.LastIndexOf(':');
        if (colon < 0)
            return (spec, 1);
        return (spec[..colon], double.Parse(spec[(colon + 1)..], CultureInfo.InvariantCulture));
    }

    private Button BuildKey(string label)
    {
        var key = new Button { Content = label, Height = 44 };
        key.SetResourceReference(StyleProperty, "KeyCapStyle");
        switch (label)
        {
            case "⌫":
                key.Click += (_, _) => Backspace();
                break;
            case "Space":
                key.Click += (_, _) => Space();
                break;
            case "Shift":
                _shiftKey = key;
                key.Click += (_, _) => { _shift = !_shift; RefreshCase(); };
                break;
            case "Caps":
                _capsKey = key;
                key.Click += (_, _) => { _caps = !_caps; RefreshCase(); };
                break;
            default:
                char c = label[0];
                if (char.IsLetter(c))
                    _letters.Add((key, c));
                key.Click += (_, _) => Type(c);
                break;
        }
        return key;
    }

    private void Type(char c)
    {
        if (_text.Length >= MaxLength)
            return;
        if (char.IsLetter(c))
        {
            _text += _shift ^ _caps ? char.ToUpperInvariant(c) : c;
            // Shift is one letter's worth.
            if (_shift)
            {
                _shift = false;
                RefreshCase();
            }
        }
        else
        {
            _text += c;
        }
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

    private bool AtWordStart() => _text.Length == 0 || _text[^1] == ' ';

    // Letter caps follow the case they would type; Shift and Caps light up
    // (the key style's Tag underline) while they are on.
    private void RefreshCase()
    {
        bool upper = _shift ^ _caps;
        foreach (var (key, letter) in _letters)
            key.Content = (upper ? char.ToUpperInvariant(letter) : letter).ToString();
        if (_shiftKey != null)
            _shiftKey.Tag = _shift;
        if (_capsKey != null)
            _capsKey.Tag = _caps;
    }

    private void UpdatePreview()
    {
        PreviewText.Text = _text;
        PlaceholderText.Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        string trimmed = _text.Trim();
        DoneButton.IsEnabled = trimmed.Length > 0 && (_isAcceptable?.Invoke(trimmed) ?? true);
    }
}
