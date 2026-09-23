# UnboundKeys Manual

How UnboundKeys behaves. See [README.md](README.md) for what it's for and how to install it.

## Voice Keys commands

Say "press" `one` through `ten`, or `stop`. Nothing else is recognized; speech recognition runs fully offline and its vocabulary is limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` map to the number keys, and `ten` maps to `0`. Every word's key, and how it behaves, is remappable from the dashboard's **Voice Keys** button (left-click the skull icon).

## Mouse Keys commands

Right Click, Middle Click, Mouse 4, Mouse 5, Wheel Up, and Wheel Down can each be remapped to a keyboard key too, from the dashboard's **Mouse Keys** button. Left Click is never remappable — it's the one gesture that always opens the dashboard, so there's never a mapping that can lock you out of reaching it. Mouse buttons are always independently active alongside Voice Keys.

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

Because Infinite holds/repeats can run indefinitely, UnboundKeys is built so a stuck key is never the only way out:

- Voice Keys, Mouse Keys, and the virtual keyboard each get their own independent set of slots: only one *word*, one *mouse button*, and one *virtual key* can each be doing an infinite hold at a time — starting a new one only bumps whichever one of the same kind had that slot before, never the other kinds. The same split applies to infinite repeat, so all three can be infinite-holding (or infinite-repeating) at once without interfering with each other.
- Running an infinite-repeat on more than one of them at once doesn't queue or block any of them — they're independent loops, each tapping its own key on its own timer.
- Saying **"press stop"**, or double-tapping the physical **Caps Lock** key (within about a second), releases everything currently held or repeating — Voice Keys, Mouse Keys, and the virtual keyboard (including any sticky Shift/Ctrl/Alt/Win) all at once. Works no matter what, even if nothing at all is mapped, and is never suppressed, so it never interferes with what's in focus. Two taps rather than three or one is deliberate: two real toggles put Caps Lock's own on/off state right back where it started, with no lingering side effect to notice or undo.
- Pausing listening, closing UnboundKeys, or switching away from the game window (alt-tab, or switching browser tabs) all release everything automatically too.

## Profiles

Each profile has its own full set of key mappings — the ten words, the six mouse buttons, and the virtual keyboard's remappable keys — useful for different games, or different setups. Switching profiles swaps everything over at once. Up to 10 profiles total.

## Saved data

Everything is saved to `%AppData%\UnboundKeys\settings.json` and reloaded automatically next launch.

## Troubleshooting

**A mapped mouse button (or virtual keyboard key) does nothing in a specific game — the game still reacts to the real click/key instead of the mapped one, even though Voice Keys works fine in the same game.** Try running UnboundKeys as Administrator (right-click its shortcut → **Run as administrator**). Some games require elevated privileges to run, and Windows can prevent a non-elevated app's input remapping from affecting an elevated one — UnboundKeys needs to be running at least as high a privilege level as the game. If running as Administrator doesn't fix it, the game's anti-cheat (EasyAntiCheat, BattlEye, Vanguard, etc.) may be specifically blocking the kind of low-level input hook UnboundKeys uses, since it's the same technique some cheat tools use — that's not something UnboundKeys can work around.
