# UnboundKeys backlog

Notes for a future session — not urgent. Written after a full-codebase
review at v2.0.0. **Do this only after in-game testing of 2.0.0 confirms
mouse remapping + the dashboard rewrite hold up in practice** — no sense
refactoring code that might still need behavior changes from real testing.

## Feature ideas

- **Predictive text for the virtual keyboard.** Fizzil uses this regularly
  on the Windows on-screen keyboard and wants it here too — a
  semi-transparent suggestion strip above the keyboard, click a suggestion
  (or press the shown letter) to type the rest of the word. Explicitly
  descoped for now (2026-09-23) — see the conversation for the full
  reasoning, summarized:
  - True Windows-OSK-style prediction reads the actual focused field's
    text buffer via Text Services Framework/IME-level integration. Not
    feasible here without a much bigger architecture change:
    `VirtualKeyboardForm` is a `NonActivatingForm` that deliberately never
    takes focus and just blindly `SendInput`s keystrokes to whatever
    window currently has it — it has no visibility into what actually
    lands there, by design (that's what makes it safe to click through
    mid-game).
  - A scoped-down version IS realistic: track only the letters *we*
    send (we're the one calling `NativeInput.TapKey`), keep a local
    "current word" guess built from that, and suggest completions from a
    dictionary. Fizzil is fine with the caveat that this can silently
    drift out of sync whenever focus/cursor moves outside our control
    (clicking elsewhere, physical typing, the target app's own
    autocomplete) — confirmed the real Windows OSK has the exact same
    "loses sync on focus change" limitation, so this isn't a new problem
    being introduced.
  - The actual speed win Fizzil described: type a letter (e.g. "T"), a
    predicted word appears, pressing one more key (e.g. "A" for the next
    letter of a *different* predicted word, or some dedicated accept key)
    commits it — worth designing the interaction around that specific
    flow rather than assuming "click a suggestion button" is the only UI.

## Worth doing

- ~~Collapse the WordCardTab/MouseButtonCardTab duplication~~ — **done.**
  The two ~90%-identical card files are now one `RemapCardTab.cs`, driven
  through a small `IRemapSource` interface (`KeyMapSource`/`MouseMapSource`
  adapters forward to the existing static `KeyMap`/`MouseMap` classes,
  which weren't touched). This was the highest-risk half of the original
  duplication item — the specific place a real drift bug (Repeat Interval
  visibility) already almost happened.

- ~~Collapse the `KeyMap.cs`/`MouseMap.cs`/`PhysicalKeyMap.cs`
  duplication~~ — **moot.** Physical Press was removed entirely once the
  virtual on-screen keyboard made physically remapping the number row
  redundant, taking `PhysicalKeyMap.cs`/`PhysicalKeyCatalog.cs` with it.
  `KeyMap`/`MouseMap`/`VirtualKeyMap` remain (a fourth similar file, not a
  third), but that trio was never flagged as needing a merge — revisit
  only if a real drift bug shows up between them, same bar as everything
  else on this list.

## Minor cleanup, low priority

- ~~`ActionBar.cs`'s dead `onWidthChanged` callback~~ — **done.**
- ~~`ProfilesTab.cs` hand-rolling its own confirm-delete button~~ — **done,**
  now uses `Theme.MakeConfirmDeleteButton`.

## Deliberately not on this list

`Settings.cs` re-reading/re-writing the whole settings file on every
dashboard click, and `Program.cs` firing an uncapped `Task.Run` per
press — both technically "inefficient" but harmless given how this app is
actually used (configuring occasionally, not per-frame during gameplay).
Not worth touching.

## Don't refactor without re-reading first

`DashboardForm.cs`'s `Region`-based window shaping / `WM_SETREDRAW`
flicker suppression, and `KeyExecutor.cs`'s `LooksLikeOwnSideEffect` title
heuristic, look like heavy machinery for what they do. They're not
premature complexity — each is a direct, hard-won fix for a specific bug
(dashboard flashing/jumping; infinite repeat stopping itself after a few
taps). A simpler-looking version was tried and failed for each. Read the
comments in place before touching either.
