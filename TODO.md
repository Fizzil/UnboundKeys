# UnboundKeys backlog

Notes for a future session — not urgent.

## Done in 4.0.0

- ~~Predictive text for the on-screen keyboard~~ — **done**, in the scoped
  form described earlier: `WordPredictor` tracks the letters the keyboard
  itself sends, suggests completions from an embedded frequency list
  plus the words actually typed, and a click types the rest of the word
  and a space. It has the same "loses sync when focus moves" limitation
  as Windows' own on-screen keyboard, which was the accepted trade-off.
- ~~The WinForms dashboard~~ — replaced by the WPF dashboard (Wpf/,
  Themes/); the overlay skull is gone in favour of a tray icon and
  "press menu".

## Ideas

- **An icon of its own.** The skull (Assets/skull.ico) is still the app
  and tray icon; the rest of the app has moved on from that look.
- **Screenshots for the README.** The old ones in Assets/screenshots are
  of the WinForms UI and are no longer linked.
- **Per-monitor DPI.** The app is system-DPI-aware; a second monitor at
  a different scale will render blurry there.
- **Suggestion accept key.** Fizzil's original idea for the suggestion
  strip was that typing the next letter of a *different* suggested word
  narrows, and a dedicated key accepts — worth trying once the click-to-
  accept version has been lived with.

## Deliberately not on this list

`Settings.cs` re-reading/re-writing the whole settings file on every
dashboard click, and `Program.cs` firing an uncapped `Task.Run` per
press — both technically "inefficient" but harmless given how this app is
actually used (configuring occasionally, not per-frame during gameplay).
Not worth touching.

## Don't refactor without re-reading first

`KeyExecutor.cs`'s `LooksLikeOwnSideEffect` title heuristic looks like
heavy machinery for what it does. It's not premature complexity — it's a
direct, hard-won fix for a specific bug (infinite repeat stopping itself
after a few taps). A simpler-looking version was tried and failed. Read
the comments in place before touching it.
