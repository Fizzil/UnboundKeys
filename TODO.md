# VoicePress backlog

Notes for a future session — not urgent. Written after a full-codebase
review at v2.0.0. **Do this only after in-game testing of 2.0.0 confirms
mouse remapping + the dashboard rewrite hold up in practice** — no sense
refactoring code that might still need behavior changes from real testing.

## Worth doing

- ~~Collapse the WordCardTab/MouseButtonCardTab duplication~~ — **done.**
  The two ~90%-identical card files are now one `RemapCardTab.cs`, driven
  through a small `IRemapSource` interface (`KeyMapSource`/`MouseMapSource`
  adapters forward to the existing static `KeyMap`/`MouseMap` classes,
  which weren't touched). This was the highest-risk half of the original
  duplication item — the specific place a real drift bug (Repeat Interval
  visibility) already almost happened.

- **Collapse the `KeyMap.cs`/`MouseMap.cs`/`PhysicalKeyMap.cs` duplication**
  (deliberately deferred twice now, not done alongside either the card
  merge or Physical Press). These are ~85% identical (~570 lines total
  across three files) but much shorter and simpler than the card files
  were — mostly mechanical CRUD, less prone to silent drift. The real cost
  of doing it: all three are referenced directly from `VoiceEngine.cs`
  (grammar building), `MouseInputWatcher.cs`/`PhysicalKeyWatcher.cs`
  (suppression checks), `ProfilesTab.cs` (profile creation),
  `KeyExecutor.cs`, and `Program.cs` — a wider blast radius than the card
  merge for a smaller LOC payoff. There's also a real semantic split to
  reconcile: `MouseMap` and `PhysicalKeyMap` both have an `Enabled`/
  unmapped state that `KeyMap` doesn't — meaning those two could likely
  share one generic implementation immediately, with `KeyMap` staying the
  odd one out. `Settings.cs`'s three parallel `Load*`/`Save*` groups
  should fold into this same pass if it happens, since they exist for the
  same reason. **Now backed by a concrete third duplicate instead of a
  hypothetical one — worth prioritizing sooner than "eventually."**

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
