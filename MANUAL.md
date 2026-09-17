# VoicePress Manual

How VoicePress behaves. See [README.md](README.md) for what it's for and how to install it.

## Voice commands

Say "press" `one` through `ten`, or `stop`. Nothing else is recognized; speech recognition runs fully offline and its vocabulary is limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` map to the number keys, and `ten` maps to `0`. Every word's key, and how it behaves, is remappable from the dashboard (right-click the skull icon).

## Mouse button commands

Right Click, Middle Click, Mouse 4, Mouse 5, Wheel Up, and Wheel Down can each be remapped to a keyboard key too, from the same dashboard. Left Click is never remappable — it's deliberately left alone so clicking always works, no matter what else is configured.

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

- Voice words and mouse buttons each get their own independent set of slots: only one *word* can be doing an infinite hold at a time, and separately only one *button* can — starting a new one only bumps whichever word/button of the same kind (voice or mouse) had that slot before, never the other kind. The same split applies to infinite repeat. In practice, this means a voice word and a mouse button can each be infinite-holding (or each infinite-repeating) at the same time — one of each, not two of the same source.
- Running an infinite-repeat word and an infinite-repeat button at once doesn't queue or block either one — they're two independent loops, each tapping its own key on its own timer. If the word repeats key **A** and the button repeats key **B**, the result in practice reads as roughly `A, B, A, B, A, B...`, since both are firing at close to the same rate rather than one waiting for the other.
- Saying **"press stop"** releases everything currently held or repeating, from both sources at once.
- Pausing listening, closing VoicePress, or switching away from the game window (alt-tab, or switching browser tabs) all release everything automatically too.

## Profiles

Each profile has its own full set of key mappings — both the ten words and the six mouse buttons — useful for different games, or different setups. Switching profiles swaps everything over at once. Up to 10 profiles total.

## Saved data

Everything is saved to `%AppData%\VoicePress\settings.json` and reloaded automatically next launch.
