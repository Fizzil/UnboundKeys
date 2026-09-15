# VoicePress Manual

How VoicePress behaves. See [README.md](README.md) for what it's for and how to install it.

## Voice commands

Say "press" `one` through `ten`, or `stop`. Nothing else is recognized; speech recognition runs fully offline and its vocabulary is limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` map to the number keys, and `ten` maps to `0`. Every word's key, and how it behaves, is remappable from the dashboard (right-click the skull icon).

## How a key can behave

Each word can be set to:

- **Tap** — a single key press (the default).
- **Repeat** — taps the key repeatedly for a set duration.
- **Hold** — holds the key down for a set duration.
- **Infinite** — instead of a fixed duration, saying the word starts the hold/repeat, and saying it again stops it.

## Multiple keys per word

"Add Key," under a word's Key row, adds up to two more keys to it — enough for a three-key combo like Ctrl+Alt+Delete, or a short sequence of abilities.

- **Tap** and **Hold** press every key together, all at once.
- **Repeat** instead cycles through them one at a time — Key 1, then Key 2, then Key 3, then back to Key 1 — using the normal fixed gap between each, unless "Repeat Interval" is turned on to set a custom gap for one or more of them.

## Safety nets

Because Infinite holds/repeats can run indefinitely, VoicePress is built so a stuck key is never the only way out:

- Only one word can be doing an infinite hold at a time, and separately only one can be doing an infinite repeat — starting a new one releases whichever word had that slot before.
- Saying **"press stop"** releases everything currently held or repeating.
- Pausing listening, closing VoicePress, or switching away from the game window (alt-tab, or switching browser tabs) all release everything automatically too.

## Profiles

Each profile has its own full set of key mappings — useful for different games, or different setups. Switching profiles swaps all ten words over at once.

## Saved data

Everything is saved to `%AppData%\VoicePress\settings.json` and reloaded automatically next launch.
