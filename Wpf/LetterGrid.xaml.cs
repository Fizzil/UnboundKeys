using System;
using System.Windows;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// Letters come out capitalized at the start of each word and lowercase
// after ("isle" → "Isle") — there's no Shift key, and a name that reads
// like a name beats one shouted in capitals.
public partial class LetterGrid
{
    private const int MaxLength = 15;

    private string _text = "";
    private Func<string, bool>? _isAcceptable;

    public event Action<string>? Done;
    public event Action? Cancelled;

    public LetterGrid()
    {
        InitializeComponent();

        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            char key = c;
            var button = new Button { Content = key.ToString() };
            button.SetResourceReference(StyleProperty, "LetterKeyStyle");
            button.Click += (_, _) => Append(key);
            KeyGrid.Children.Add(button);
        }

        SpaceKey.Click += (_, _) => Append(' ');
        BackspaceKey.Click += (_, _) =>
        {
            if (_text.Length == 0)
                return;
            _text = _text[..^1];
            UpdatePreview();
        };
        CancelButton.Click += (_, _) => Cancelled?.Invoke();
        DoneButton.Click += (_, _) => Done?.Invoke(_text.Trim());

        UpdatePreview();
    }

    // Starts an edit: the current name (or "" for a new one) and the
    // caller's rule for whether a spelled name may be accepted — used to
    // keep Done disabled while the name is empty or already taken.
    public void Begin(string initialText, Func<string, bool> isAcceptable)
    {
        _text = initialText;
        _isAcceptable = isAcceptable;
        UpdatePreview();
    }

    private void Append(char c)
    {
        if (_text.Length >= MaxLength)
            return;

        if (c == ' ')
        {
            if (_text.Length == 0 || _text[^1] == ' ')
                return;
            _text += ' ';
        }
        else if (char.IsLetter(c))
        {
            bool wordStart = _text.Length == 0 || _text[^1] == ' ';
            _text += wordStart ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
        }
        else
        {
            _text += c;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        PreviewText.Text = _text;
        PlaceholderText.Visibility = _text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        string trimmed = _text.Trim();
        DoneButton.IsEnabled = trimmed.Length > 0 && (_isAcceptable?.Invoke(trimmed) ?? true);
    }
}
