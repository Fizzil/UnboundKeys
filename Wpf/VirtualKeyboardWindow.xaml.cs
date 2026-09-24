using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// WPF port of VirtualKeyboardForm.cs (WinForms) — the floating keyboard
// for typing with a mouse. Same layout, same behavior: a key fires the
// instant it's pressed and repeats while held; Shift/Ctrl/Alt/Win are
// sticky (see StickyModifiers) so combos work one click at a time; the
// digits and letters are remappable (VirtualKeyMap) and wear a faint
// accent wash once customized; Caps tracks its own state and doubles as
// the panic button (two quick clicks release everything and lift Fade);
// Mini collapses it to one quick-access strip. What's gone is the
// right-click-to-remap path — a remapped right button can't right-click
// anything in this app, so remapping lives on the dashboard's Keyboard
// page instead.
public partial class VirtualKeyboardWindow
{
    private const double RowHeight = 44;
    private const ushort VkCapital = 0x14;
    private const int RepeatInitialDelayMs = 450;
    private const int RepeatIntervalMs = 100; // matches KeyExecutor's own RepeatIntervalMs
    private const int PanicTapWindowMs = 1000;
    // Approximate outer size, for keeping a remembered position on screen
    // before the window has measured itself.
    private const double ApproxWidth = 800;
    private const double ApproxFullHeight = 230;
    private const double ApproxMiniHeight = 58;

    private enum KeyKind { Plain, Remappable, StickyFixed, MiniToggle }

    private readonly record struct KeySpec(string Label, KeyKind Kind, string? Id, ushort Vk, bool Extended, double Width, string? ShiftLabel, bool LargeLabel = false);

    private static KeySpec Plain(string label, ushort vk, bool extended, double width, string? shiftLabel = null, bool largeLabel = false) =>
        new(label, KeyKind.Plain, null, vk, extended, width, shiftLabel, largeLabel);

    private static KeySpec Remap(string id, string label, double width, string? shiftLabel = null) =>
        new(label, KeyKind.Remappable, id, 0, false, width, shiftLabel);

    private static KeySpec StickyFixed(string id, string label, ushort vk, bool extended, double width) =>
        new(label, KeyKind.StickyFixed, id, vk, extended, width, null);

    private static KeySpec Toggle(string label, double width) =>
        new(label, KeyKind.MiniToggle, null, 0, false, width, null);

    private readonly List<(Button Button, string Id)> _stickyButtons = new();
    private readonly List<(Border Wash, string Id)> _remappableWashes = new();
    private readonly List<(TextBlock Label, string Lower)> _letterLabels = new();
    private readonly List<(TextBlock Label, string Base, string Shifted)> _shiftableLabels = new();
    private readonly List<Button> _capsLockButtons = new();

    // Tracked locally rather than re-read from Windows after every click —
    // our own injected Caps Lock keystroke lands in whichever window has
    // real focus (never this one), and asking the OS back immediately
    // raced with that. Since there's no physical keyboard here, this
    // button is the only thing that ever toggles it. Read once, cold, on
    // open.
    private bool _capsLockOn;
    private int _capsLockPanicTapCount;
    private DateTime _lastCapsLockPanicTap = DateTime.MinValue;

    private bool _isMini;
    private readonly Action _onStickyChanged;
    private readonly Action _onFadeChanged;

    // The word being typed, as far as this keyboard can tell: letters and
    // digits it has sent since the last space/Enter/punctuation. Same
    // limitation as Windows' own on-screen keyboard — a click elsewhere,
    // or an arrow key, and it loses the thread until the next word.
    private readonly StringBuilder _currentWord = new();
    private readonly DispatcherTimer _learnedSaveTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    public bool IsMini => _isMini;

    public VirtualKeyboardWindow(bool startMini)
    {
        InitializeComponent();
        _isMini = startMini;

        foreach (var row in new[] { Row1(), Row2(), Row3(), Row4(), Row5() })
            FullLayout.Children.Add(BuildRow(row));
        MiniLayout.Children.Add(BuildRow(MiniRow()));
        ApplyMiniMode();
        ApplyScale(Settings.LoadKeyboardScale());

        _capsLockOn = System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock);

        // Both events can fire from other threads ("press stop" arrives on
        // the voice thread and releases the sticky modifiers; a panic tap
        // on a real keyboard lifts Fade from the hook thread), so their
        // handlers hop to this window's thread first.
        _onStickyChanged = () => Dispatcher.InvokeAsync(RefreshStickyHighlights);
        _onFadeChanged = () => Dispatcher.InvokeAsync(ApplyFade);
        StickyModifiers.Changed += _onStickyChanged;
        FadeMode.Changed += _onFadeChanged;

        ApplyFade();
        RefreshStickyHighlights();
        RefreshCapsLockHighlight();
        RefreshCustomizedIndicators();
        RefreshSuggestions();

        _learnedSaveTimer.Tick += (_, _) => WordPredictor.Save();
        _learnedSaveTimer.Start();

        Loaded += (_, _) => KeepOnScreen();
        Closed += (_, _) =>
        {
            StickyModifiers.Changed -= _onStickyChanged;
            FadeMode.Changed -= _onFadeChanged;
            _learnedSaveTimer.Stop();
            // Never leave a real Shift/Ctrl/Alt/Win stuck down system-wide
            // just because the keyboard was closed mid-combo.
            StickyModifiers.ReleaseAll();
            WordPredictor.Save();
            SavePlacement();
        };
    }

    // ---- Layout (identical to VirtualKeyboardForm's rows; see that file
    // for the reasoning behind each width) ----

    private static KeySpec[] Row1() => new[]
    {
        Plain("Esc", 0x1B, false, 1.2),
        Plain("`", 0xC0, false, 0.8, "~"),
        Remap("v1", "1", 1, "!"),
        Remap("v2", "2", 1, "@"),
        Remap("v3", "3", 1, "#"),
        Remap("v4", "4", 1, "$"),
        Remap("v5", "5", 1, "%"),
        Remap("v6", "6", 1, "^"),
        Remap("v7", "7", 1, "&"),
        Remap("v8", "8", 1, "*"),
        Remap("v9", "9", 1, "("),
        Remap("v0", "0", 1, ")"),
        Plain("-", 0xBD, false, 1, "_"),
        Plain("=", 0xBB, false, 1, "+"),
        Plain("⌫", 0x08, false, 1),
        Toggle("Mini", 1.4),
    };

    private static KeySpec[] Row2() => new[]
    {
        Plain("Tab", 0x09, false, 1.5),
        Remap("vq", "q", 1),
        Remap("vw", "w", 1),
        Remap("ve", "e", 1),
        Remap("vr", "r", 1),
        Remap("vt", "t", 1),
        Remap("vy", "y", 1),
        Remap("vu", "u", 1),
        Remap("vi", "i", 1),
        Remap("vo", "o", 1),
        Remap("vp", "p", 1),
        Plain("[", 0xDB, false, 1, "{"),
        Plain("]", 0xDD, false, 1, "}"),
        Plain("\\", 0xDC, false, 1.35, "|"),
        Plain("Del", 0x2E, true, 1.65),
    };

    private static KeySpec[] Row3() => new[]
    {
        Plain("Caps", 0x14, false, 1.8),
        Remap("va", "a", 1),
        Remap("vs", "s", 1),
        Remap("vd", "d", 1),
        Remap("vf", "f", 1),
        Remap("vg", "g", 1),
        Remap("vh", "h", 1),
        Remap("vj", "j", 1),
        Remap("vk", "k", 1),
        Remap("vl", "l", 1),
        Plain(";", 0xBA, false, 1, ":", largeLabel: true),
        Plain("'", 0xDE, false, 1, "\"", largeLabel: true),
        Plain("Enter", 0x0D, false, 2.2),
    };

    private static KeySpec[] Row4() => new[]
    {
        StickyFixed("vshift", "Shift", 0x10, false, 2.8),
        Remap("vz", "z", 1),
        Remap("vx", "x", 1),
        Remap("vc", "c", 1),
        Remap("vv", "v", 1),
        Remap("vb", "b", 1),
        Remap("vn", "n", 1),
        Remap("vm", "m", 1),
        Plain(",", 0xBC, false, 1, "<", largeLabel: true),
        Plain(".", 0xBE, false, 1, ">"),
        Plain("/", 0xBF, false, 1, "?"),
        StickyFixed("vshift", "Shift", 0x10, false, 2.8),
    };

    private static KeySpec[] Row5() => new[]
    {
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5),
        StickyFixed("vwin", "Win", 0x5B, true, 1.5),
        StickyFixed("valt", "Alt", 0x12, false, 1.5),
        Plain("", 0x20, false, 5),
        StickyFixed("valt", "Alt", 0x12, false, 1.5),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5),
        Plain("←", 0x25, true, 1),
        Plain("↓", 0x28, true, 1),
        Plain("↑", 0x26, true, 1),
        Plain("→", 0x27, true, 1),
    };

    // The collapsed strip — none of these are remappable, which is the
    // point of collapsing the letters and digits away. Order is Fizzil's.
    private static KeySpec[] MiniRow() => new[]
    {
        Plain("Esc", 0x1B, false, 1),
        Plain("Tab", 0x09, false, 1),
        Plain("Caps", 0x14, false, 1),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1),
        StickyFixed("vwin", "Win", 0x5B, true, 1),
        StickyFixed("valt", "Alt", 0x12, false, 1),
        Plain("Space", 0x20, false, 1),
        Plain("←", 0x25, true, 1),
        Plain("↓", 0x28, true, 1),
        Plain("↑", 0x26, true, 1),
        Plain("→", 0x27, true, 1),
        Plain("Enter", 0x0D, false, 1),
        Plain("Del", 0x2E, true, 1),
        Plain("⌫", 0x08, false, 1),
        Toggle("Maxi", 1),
    };

    private Grid BuildRow(KeySpec[] specs)
    {
        var row = new Grid { Height = RowHeight };
        foreach (var spec in specs)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spec.Width, GridUnitType.Star) });

        for (int i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];

            var label = new TextBlock
            {
                Text = spec.Label,
                FontSize = spec.LargeLabel ? 16 : 13,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // The "this key is customized" wash: a faint accent layer under
            // the label, over the whole cap (see RefreshCustomizedIndicators).
            var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.24, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            wash.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            var content = new Grid();
            content.Children.Add(wash);
            content.Children.Add(label);

            var button = new Button { Content = content };
            button.SetResourceReference(StyleProperty, "KeyCapStyle");
            Grid.SetColumn(button, i);
            row.Children.Add(button);
            WireKey(button, spec);

            if (spec.Kind == KeyKind.Remappable)
                _remappableWashes.Add((wash, spec.Id!));
            if (spec.Kind == KeyKind.StickyFixed)
                _stickyButtons.Add((button, spec.Id!));
            // A remappable letter specifically ("va".."vz") — the digits'
            // ids ("v1".."v0") have a digit second, so they're skipped.
            if (spec.Kind == KeyKind.Remappable && spec.Id is { Length: 2 } id && id[0] == 'v' && char.IsLower(id[1]))
                _letterLabels.Add((label, spec.Label));
            if (spec.ShiftLabel != null)
                _shiftableLabels.Add((label, spec.Label, spec.ShiftLabel));
            if (spec.Kind == KeyKind.Plain && spec.Vk == VkCapital)
                _capsLockButtons.Add(button);
        }

        return row;
    }

    // Press on the way DOWN (a real key types the instant it's pressed),
    // repeat while held, and finish the gesture on the way up — the sticky
    // modifiers are consumed on release, not per press, so a held Shift
    // covers a whole run of repeated capitals. Preview events rather than
    // Click: Click would only fire on release, and only inside the key.
    private void WireKey(Button button, KeySpec spec)
    {
        DispatcherTimer? repeatTimer = null;
        bool pressed = false;

        void PerformPress()
        {
            switch (spec.Kind)
            {
                case KeyKind.Remappable:
                    string id = spec.Id!;
                    Task.Run(() => KeyExecutor.Execute(id, VirtualKeyMap.GetAllKeys(id), VirtualKeyMap.Behaviors[id]));
                    TrackRemappableKey(id, spec.Label);
                    break;
                case KeyKind.Plain:
                    NativeInput.TapKey(spec.Vk, spec.Extended);
                    if (spec.Vk == VkCapital)
                    {
                        _capsLockOn = !_capsLockOn;
                        RefreshCapsLockHighlight();
                        HandleCapsLockPanicTap();
                    }
                    TrackPlainKey(spec.Vk);
                    break;
            }
        }

        // Caps toggles rather than types, so holding it isn't "toggle
        // repeatedly"; a remappable key only auto-repeats while its own
        // mapping has no Repeat/Hold/Infinite — if it does, one press
        // already runs that whole behavior, and a mechanical repeat on top
        // would just fight it.
        bool CanRepeat() => spec.Kind switch
        {
            KeyKind.Plain => spec.Vk != VkCapital,
            KeyKind.Remappable => VirtualKeyMap.Behaviors[spec.Id!] is { Repeat: false, Hold: false, Infinite: false },
            _ => false,
        };

        void StopRepeat()
        {
            repeatTimer?.Stop();
            repeatTimer = null;
        }

        void Release()
        {
            if (!pressed)
                return;
            pressed = false;
            StopRepeat();

            switch (spec.Kind)
            {
                case KeyKind.StickyFixed:
                    // Its highlight follows the modifier's real state via
                    // StickyModifiers.Changed, not this button's own press.
                    StickyModifiers.Toggle(spec.Id!, new List<(ushort, bool)> { (spec.Vk, spec.Extended) });
                    break;
                case KeyKind.Remappable:
                    StickyModifiers.ConsumeForKeyPress();
                    button.Tag = false;
                    break;
                case KeyKind.Plain:
                    StickyModifiers.ConsumeForKeyPress();
                    // Caps keeps showing its toggle state, like a keyboard's LED.
                    if (spec.Vk != VkCapital)
                        button.Tag = false;
                    break;
                case KeyKind.MiniToggle:
                    button.Tag = false;
                    ToggleMiniMode();
                    break;
            }
        }

        button.PreviewMouseLeftButtonDown += (_, _) =>
        {
            pressed = true;
            button.Tag = true;

            if (spec.Kind is not (KeyKind.Plain or KeyKind.Remappable))
                return;

            PerformPress();
            if (!CanRepeat())
                return;

            bool firstTick = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RepeatInitialDelayMs) };
            timer.Tick += (_, _) =>
            {
                if (firstTick)
                {
                    firstTick = false;
                    timer.Interval = TimeSpan.FromMilliseconds(RepeatIntervalMs);
                }
                PerformPress();
            };
            repeatTimer = timer;
            timer.Start();
        };
        button.PreviewMouseLeftButtonUp += (_, _) => Release();
        // The button captures the mouse while pressed, so a release
        // anywhere still reaches it; losing capture some other way (the
        // window being closed under it, say) must still end the gesture.
        button.LostMouseCapture += (_, _) => Release();
    }

    // ---- Word suggestions ----

    // A digit or letter key adds to the word in progress — unless it's
    // been remapped to send something else, or a sticky Ctrl/Alt/Win means
    // this is a shortcut rather than typing; either way the thread is lost
    // and the word starts over.
    private void TrackRemappableKey(string id, string label)
    {
        bool shortcut = StickyModifiers.IsActive("vctrl") || StickyModifiers.IsActive("valt") || StickyModifiers.IsActive("vwin");
        if (shortcut || VirtualKeyMap.IsCustomized(id) || label.Length != 1)
            _currentWord.Clear();
        else
            _currentWord.Append(char.ToLowerInvariant(label[0]));
        RefreshSuggestions();
    }

    private void TrackPlainKey(ushort vk)
    {
        switch (vk)
        {
            case 0x08: // Backspace
                if (_currentWord.Length > 0)
                    _currentWord.Length--;
                break;
            case 0x20: // Space
            case 0x0D: // Enter
            case 0x09: // Tab
            case 0xBA: case 0xBB: case 0xBC: case 0xBD: case 0xBE: case 0xBF: // ; = , - . /
            case 0xC0: case 0xDB: case 0xDC: case 0xDD: case 0xDE:           // ` [ \ ] '
                CommitWord();
                break;
            case VkCapital: // toggles case, doesn't move the caret
                return;
            default: // Esc, Del, the arrows — the caret moved somewhere; lose the thread
                _currentWord.Clear();
                break;
        }
        RefreshSuggestions();
    }

    private void CommitWord()
    {
        if (_currentWord.Length > 0)
            WordPredictor.Learn(_currentWord.ToString());
        _currentWord.Clear();
    }

    private void RefreshSuggestions()
    {
        SuggestionRow.Children.Clear();
        foreach (var word in WordPredictor.Suggest(_currentWord.ToString()))
        {
            string suggestion = word;
            var button = new Button
            {
                Content = new TextBlock { Text = suggestion, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
                FontSize = 12,
            };
            button.SetResourceReference(StyleProperty, "KeyCapStyle");
            button.Click += (_, _) => CompleteWith(suggestion);
            SuggestionRow.Children.Add(button);
        }
    }

    // Types the rest of the chosen word and a space, the way Windows' own
    // suggestions do. Plain taps, so Caps Lock (or a sticky Shift) applies
    // to them the same as it would to typed letters; the sticky modifiers
    // are consumed afterward like any other key press.
    private void CompleteWith(string word)
    {
        string rest = word[Math.Min(_currentWord.Length, word.Length)..];
        foreach (char c in rest)
        {
            ushort vk = char.IsAsciiDigit(c) ? (ushort)c : (ushort)char.ToUpperInvariant(c);
            NativeInput.TapKey(vk, false);
        }
        NativeInput.TapKey(0x20, false);
        StickyModifiers.ConsumeForKeyPress();

        WordPredictor.Learn(word);
        _currentWord.Clear();
        RefreshSuggestions();
    }

    // ---- State display ----

    private void RefreshStickyHighlights()
    {
        foreach (var (button, id) in _stickyButtons)
            button.Tag = StickyModifiers.IsActive(id);

        RefreshLetterCase();

        bool shiftActive = StickyModifiers.IsActive("vshift");
        foreach (var (label, baseText, shifted) in _shiftableLabels)
            label.Text = shiftActive ? shifted : baseText;
    }

    // Uppercase whenever exactly one of Caps/Shift is on — Shift inverts
    // Caps rather than always winning, matching what actually gets typed.
    private void RefreshLetterCase()
    {
        bool upper = _capsLockOn ^ StickyModifiers.IsActive("vshift");
        foreach (var (label, lower) in _letterLabels)
            label.Text = upper ? lower.ToUpperInvariant() : lower;
    }

    private void RefreshCapsLockHighlight()
    {
        foreach (var button in _capsLockButtons)
            button.Tag = _capsLockOn;
        RefreshLetterCase();
    }

    // Called by the dashboard after anything that can change a key's
    // mapping (an editor closing, a profile switch, Reset All), as well as
    // on open.
    public void RefreshCustomizedIndicators()
    {
        foreach (var (wash, id) in _remappableWashes)
            wash.Visibility = VirtualKeyMap.IsCustomized(id) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyFade() => Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;

    // The whole keyboard drawn at a fraction of its designed size — a
    // LayoutTransform, so the window (SizeToContent) shrinks with it and
    // the text stays crisp. Small ≈ the Windows on-screen keyboard's own
    // footprint. Re-checked against the screen edges once it has resized.
    public void ApplyScale(double scale)
    {
        Root.LayoutTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        Dispatcher.InvokeAsync(KeepOnScreen, DispatcherPriority.Loaded);
    }

    private void ToggleMiniMode()
    {
        _isMini = !_isMini;
        ApplyMiniMode();
        SavePlacement();
    }

    private void ApplyMiniMode()
    {
        FullLayout.Visibility = _isMini ? Visibility.Collapsed : Visibility.Visible;
        MiniLayout.Visibility = _isMini ? Visibility.Visible : Visibility.Collapsed;
    }

    // Two quick clicks of Caps: release everything (KeyExecutor.ReleaseAll)
    // and lift Fade — the panic button a mouse-only user actually has, so
    // it's also the way out of a screen too faded to find the Fade switch.
    private void HandleCapsLockPanicTap()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCapsLockPanicTap).TotalMilliseconds > PanicTapWindowMs)
            _capsLockPanicTapCount = 0;
        _capsLockPanicTapCount++;
        _lastCapsLockPanicTap = now;

        if (_capsLockPanicTapCount < 2)
            return;

        _capsLockPanicTapCount = 0;
        Task.Run(KeyExecutor.ReleaseAll);
        FadeMode.TurnOff();
    }

    // ---- Placement ----

    // Where it was last left, kept on screen; the first time, just below
    // the dashboard if that fits, else along the bottom of the work area.
    public void PlaceNear(Window? anchor)
    {
        double width = ApproxWidth;
        double height = _isMini ? ApproxMiniHeight : ApproxFullHeight;
        var (savedLeft, savedTop, _) = Settings.LoadKeyboardPlacement();

        if (savedLeft is double left && savedTop is double top)
        {
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width);
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height);
            return;
        }

        var area = SystemParameters.WorkArea;
        if (anchor != null && anchor.Top + anchor.ActualHeight + 8 + height <= area.Bottom)
        {
            Left = Math.Clamp(anchor.Left, area.Left, area.Right - width);
            Top = anchor.Top + anchor.ActualHeight + 8;
            return;
        }

        Left = area.Left + (area.Width - width) / 2;
        Top = area.Bottom - height - 16;
    }

    private void SavePlacement()
    {
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
            Settings.SaveKeyboardPlacement(Left, Top, _isMini);
    }

    // DragMove runs the native move loop, which honors the no-activate
    // style; it returns once the drag ends, so the new spot is saved then.
    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
        KeepOnScreen();
        SavePlacement();
    }
}
