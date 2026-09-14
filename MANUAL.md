# VoicePress Manual

A factual reference for exactly how VoicePress behaves. See [README.md](README.md) for what it's for and how to install it.

## Voice commands

- The trigger phrase is **"press &lt;word&gt;"** — nothing else is recognized. Valid words: `one` through `ten`, and `stop`.
- Speech recognition (Vosk) runs fully offline, and its vocabulary is restricted to only these words plus "press" — it is not merely unlikely but structurally incapable of transcribing anything else, which is what keeps ordinary conversation from triggering a command.
- By default, `one`–`nine` map to the number keys **1–9**, and `ten` maps to **0** (a game hotbar layout). Every word's key is fully remappable per profile via the dashboard.

## Key behavior, per word

Each of the ten words has its own independent settings:

- **Tap** (default): sends a single key press.
- **Repeat**: taps the key repeatedly for a set duration, capped at 10 times per second.
- **Hold**: holds the key down for a set duration.
- **Infinite**: instead of running for a fixed duration, saying the word starts the hold/repeat; saying it again stops it.
- **Duration**: adjusted with `+1`, `+0.1`, and a reset button. Changing the duration in any way while Infinite is on automatically turns Infinite off and releases the key, so the dashboard and the actual behavior never disagree.
- **Repeat and Hold are mutually exclusive** per word — turning one on turns the other off, and switching between them resets that word's timer and Infinite setting back to default rather than carrying over what the other mode had configured.

## Safety nets

VoicePress is built so a stuck key can never be the only way a session ends:

- **One infinite hold, one infinite repeat, at a time.** Only one word may be doing an infinite hold at once, and separately only one word may be doing an infinite repeat — these are two independent slots (one word can hold while a different word repeats). Starting a new one bumps and releases whichever word previously held that slot.
- **"Press stop"** releases every currently-engaged infinite hold/repeat immediately — a voice-only safety net for when clicking isn't an option.
- **Pausing listening** (left-click the skull icon) releases everything currently engaged.
- **Closing VoicePress** releases everything before it exits.
- **Changing focus** — alt-tabbing to a different window, or (for apps like browsers where switching tabs doesn't change the window itself) the same window's title text changing — automatically releases any active infinite hold/repeat. Checked about every 300ms.

## The dashboard

Right-click the skull icon to open it; right-click again to close it.

- Opens collapsed, with no tab selected.
- Tabs, left to right: **Profile**, **Press** (a static label, not clickable), **1–10** (one per word), then the listener icon pinned to the right.
- Clicking a tab opens its card. Clicking the *same* tab again collapses it; clicking a *different* tab always opens fully.
- Each word's card has: **Key** (click to browse categories, hover a category to see its individual keys, click one to assign it), **Repeat**, **Hold**, a duration readout with `+1`/`+0.1`/reset, an **Infinite** toggle, and **Reset** (returns that one word to its original key, with Repeat/Hold/duration/Infinite all cleared).
- Tapping **Reset** three times quickly (within 1.5s) reveals a **Reset All** button that resets every one of the ten cards in the current profile at once.
- If you drag the listener icon while a key-category popup is open, the popup follows along with the dashboard instead of being left behind.

## Profiles

- Each profile has its own complete, independent set of all ten words' key mappings and behaviors.
- **Default** always exists and can't be deleted.
- **Add Profile** → type a name → confirm creates a new profile starting from the original default key layout (1–9, then 0).
- Clicking a profile switches to it immediately (all ten cards reload) but keeps you on the Profiles tab — it doesn't jump you to tab 1.
- Deleting a profile takes two taps on its "✕" (tap once to arm, tap again within 2 seconds to confirm) — a safety net against an accidental delete. Deleting the currently active profile falls back to Default.
- Switching profiles releases any currently-engaged infinite hold/repeat first, since the word-to-key meanings may be different in the new profile.

## The listener icon

- **Left-click**: pause/resume listening (dimmed = paused).
- **Right-click**: open/close the dashboard.
- **Click and drag**: move the icon anywhere on screen. If the dashboard is open, it follows along, glued to the icon; both stay within the screen's edges. A press that doesn't move more than a few pixels still counts as a click, not a drag.

## Saved data

All profiles (key maps, behaviors) and which one is currently active are saved to `%AppData%\VoicePress\settings.json`, and reloaded automatically the next time VoicePress starts.
