# VoicePress backlog

Notes for a future session — not urgent. Written after a full-codebase
review at v2.0.0. **Do this only after in-game testing of 2.0.0 confirms
mouse remapping + the dashboard rewrite hold up in practice** — no sense
refactoring code that might still need behavior changes from real testing.

## Worth doing

- **Collapse the voice/mouse duplication.** `WordCardTab.cs` and
  `MouseButtonCardTab.cs` are ~90% identical (678 vs 541 lines); `KeyMap.cs`
  and `MouseMap.cs` are the same story (~85% identical); `Settings.cs` has
  two parallel sets of `Load*`/`Save*` methods for the same reason. All
  three pairs differ mainly in "which map" they point at. Together that's
  roughly 900 of the app's 4,650 lines. The fix: one generic card builder
  and one generic map class, parameterized by source (voice word vs. mouse
  button), instead of hand-copied pairs. Biggest payoff: a future behavior
  change (like Repeat Interval) only needs to happen once instead of twice
  in files that don't know about each other and can quietly drift apart —
  which already almost happened once.

## Minor cleanup, low priority

- `ActionBar.cs`'s `onWidthChanged` callback is now a no-op — a leftover
  from before the dashboard became fixed-width. Safe to remove along with
  the now-stale "the bar just grows wider" comment.
- `ProfilesTab.cs` hand-rolls its own "tap twice to confirm delete" button
  instead of reusing `Theme.MakeConfirmDeleteButton`, which already does
  the same thing.

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
