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

- **Per-monitor DPI.** The app is system-DPI-aware; a second monitor at
  a different scale will render blurry there.
- **Timing harness on a busy machine.** The timing scenarios in tests/UnboundKeys.Tests have
  failed once or twice while the machine was compiling or a permission prompt was up; a rerun
  alone passes. The settings and updater checks are not affected.
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

- **Cooldown rethink** (2026-09-27, Fizzil): a loop through several abilities wastes global cooldowns on abilities still on their own cooldown, so any shared cooldown or per-key gap presses into nothing. The right version has to know each ability's own cooldown. Game mode, the class cooldown, the haste calculator and custom gaps between keys were removed from the editor pending that; the code is in history at tag v4.2.0.
