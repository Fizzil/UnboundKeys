# VoicePress Manual

How VoicePress behaves. See [README.md](README.md) for what it's for and how to install it.

## Press: Voice or Physical

The dashboard's **Press** button opens a choice between two ways to trigger the same ten actions: **Voice Press** (spoken words) or **Physical Press** (the physical `1`–`9`/`0` number-row keys). Only one is active at a time — switching to one stops the other from doing anything at all, and releases anything it was still holding or repeating, until you switch back. This is mainly for able-bodied friends who'd rather use the physical keys than talk.

## Voice commands

Say "press" `one` through `ten`, or `stop`. Nothing else is recognized; speech recognition runs fully offline and its vocabulary is limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` map to the number keys, and `ten` maps to `0`. Every word's key, and how it behaves, is remappable from the dashboard (left-click the skull icon). Only takes effect while Voice Press is the active source (see above) — while Physical Press is active instead, a recognized word is simply not acted on.

## Physical Press commands

The physical `1`–`9` and `0` keys — the number row, not the numpad — can each be remapped the same way voice words can, from the same dashboard. A key starts unmapped (types normally) until you assign it a key; once mapped, pressing it sends the assigned key instead. Only takes effect while Physical Press is the active source (see above) — while Voice Press is active instead, every number-row key types normally regardless of what's mapped.

## Mouse button commands

Right Click, Middle Click, Mouse 4, Mouse 5, Wheel Up, and Wheel Down can each be remapped to a keyboard key too, from the same dashboard. Left Click is never remappable — it's the one gesture that always opens the dashboard, so there's never a mapping that can lock you out of reaching it. Mouse buttons aren't part of the Voice/Physical exclusivity above — they stay independently active no matter which Press source is selected.

A button starts unmapped (a normal click) until you assign it a key; once mapped, pressing it sends the key instead of the usual click.

## How a key can behave

Each word or button can be set to:

- **Tap** — a single key press (the default).
- **Repeat** — taps the key repeatedly for a set duration.
- **Hold** — holds the key down for a set duration.
- **Infinite** — instead of a fixed duration, saying the word (or pressing the button) starts the hold/repeat, and saying/pressing it again stops it.

Saying the word (or pressing the button) again while a Repeat or Hold is still running — Infinite or not — stops it early instead of waiting out its full duration. That makes a long fixed duration a usable stand-in for Infinite whenever you'd rather have a backstop maximum length than a truly indefinite hold.

## Multiple keys per word or button

"Add Key," under a word's or button's Key row, adds up to two more keys to it — enough for a three-key combo like Ctrl+Alt+Delete, or a short sequence of abilities.

- **Tap** and **Hold** press every key together, all at once.
- **Repeat** instead cycles through them one at a time — Key 1, then Key 2, then Key 3, then back to Key 1 — using the normal fixed gap between each, unless "Repeat Interval" is turned on to set a custom gap for one or more of them.

## Safety nets

Because Infinite holds/repeats can run indefinitely, VoicePress is built so a stuck key is never the only way out:

- Press and Mouse each get their own independent set of slots: only one *word or physical key* can be doing an infinite hold at a time, and separately only one *mouse button* can — starting a new one only bumps whichever one of the same kind had that slot before, never the other kind. The same split applies to infinite repeat. In practice, this means Press and a mouse button can each be infinite-holding (or each infinite-repeating) at the same time. Voice and Physical share Press's own slots rather than getting a separate pair each, since only one of them is ever active anyway (see "Press: Voice or Physical" above).
- Running an infinite-repeat on Press and an infinite-repeat mouse button at once doesn't queue or block either one — they're two independent loops, each tapping its own key on its own timer. If Press repeats key **A** and the mouse button repeats key **B**, the result in practice reads as roughly `A, B, A, B, A, B...`, since both are firing at close to the same rate rather than one waiting for the other.
- Saying **"press stop"**, or double-tapping the physical **Caps Lock** key (within about a second), releases everything currently held or repeating, from Press and Mouse at once, regardless of which Press source is active. Works no matter what — even while Voice Press is active, or if nothing at all is mapped — and is never suppressed, so it never interferes with what's in focus. Two taps rather than three or one is deliberate: two real toggles put Caps Lock's own on/off state right back where it started, with no lingering side effect to notice or undo.
- Switching Press's active source (Voice ↔ Physical) releases anything the source you're switching away from was still holding or repeating.
- Pausing listening, closing VoicePress, or switching away from the game window (alt-tab, or switching browser tabs) all release everything automatically too.

## Profiles

Each profile has its own full set of key mappings — the ten words, the ten physical keys, and the six mouse buttons — plus which of Voice/Physical Press is active, useful for different games, or different setups. Switching profiles swaps everything over at once. Up to 10 profiles total.

## Saved data

Everything is saved to `%AppData%\VoicePress\settings.json` and reloaded automatically next launch.

## Troubleshooting

**A mapped mouse button (or physical key) does nothing in a specific game — the game still reacts to the real click/key instead of the mapped one, even though Voice Press works fine in the same game.** Try running VoicePress as Administrator (right-click its shortcut → **Run as administrator**). Some games require elevated privileges to run, and Windows can prevent a non-elevated app's input remapping from affecting an elevated one — VoicePress needs to be running at least as high a privilege level as the game. If running as Administrator doesn't fix it, the game's anti-cheat (EasyAntiCheat, BattlEye, Vanguard, etc.) may be specifically blocking the kind of low-level input hook VoicePress uses, since it's the same technique some cheat tools use — that's not something VoicePress can work around.
