# VoicePress

Remap mouse buttons, voice commands, and a physical number row to keyboard keys, with full control over how each one gets pressed — a small Windows overlay built for playing **The Isle: Evrima**.

## Screenshots

<p float="left">
  <img src="Assets/screenshots/VP-PressOptions.png" width="24%" alt="Press's Voice Press/Physical Press selector, dropped down below the Press button" />
  <img src="Assets/screenshots/VP-Mouse.png" width="24%" alt="Middle Click's card, showing a 1/2/3 combo with Repeat, Repeat Interval, and the K1/K2/K3 timing row" />
  <img src="Assets/screenshots/VP-MultiKey-RepeatInterval.png" width="24%" alt="A three-key A/B/C combo with Repeat and Infinite on, and the Repeat Interval row with K1/K2/K3 timing" />
  <img src="Assets/screenshots/VP-Profiles.png" width="24%" alt="The Profile tab, showing Default and a custom 'the isle' profile, with the Press/Mouse/Profile row above it" />
</p>

## What it does

- Say "press one" through "press ten" to send the matching key — or skip voice entirely and press the physical `1`–`9`/`0` number-row keys instead, remapped the same way. Only one of Voice Press or Physical Press is active at a time, switchable right from the dashboard — mainly for able-bodied friends who'd rather use the physical keys than talk.
- Remap Right Click, Middle Click, Mouse 4/5, and Wheel Up/Down to send a key too — Left Click is left alone on purpose, so you can never lose the ability to click.
- Remap which key each word or button sends — each one taps its key by default, or can be set to hold or repeat instead — and manage multiple named profiles, left-click the skull icon for a small dashboard.
- Add up to two more keys to a word or button for a combo, like Ctrl+C — tap and hold press them all together, and repeat cycles through them in sequence, with the timing between each adjustable.
- Say "press stop," or double-tap the physical Caps Lock key, to release anything currently held down or repeating — a safety net for whenever a command can't be repeated in time. Works no matter which Press source is active, and never interferes with Caps Lock's normal function (its on/off state ends up right back where it started, since two real toggles cancel out).
- Say the word (or press the button) again while it's mid-hold or mid-repeat to stop it early, instead of waiting out its full duration — handy for setting a long duration as a stand-in for indefinite.
- Right-click the skull icon to pause listening, or drag it to move it around the screen.
- Runs fully offline — speech recognition ([Vosk](https://alphacephei.com/vosk/)) happens entirely on your machine. Nothing is sent anywhere.

See [MANUAL.md](MANUAL.md) for the full details — exact voice commands, safety nets, profile behavior, and everything the dashboard does.

## Why this exists

VoicePress was built as an accessibility tool, for Fizzil — who is disabled — to be able to play *The Isle: Evrima*, using a normal mouse or a specialized **quad mouse** (a mouse with extra physical buttons). It is **not** intended as a way to play the game hands-free with voice alone.

It's a mouse-button, voice, and physical-keyboard remapper in one: six of a mouse's buttons (Right Click, Middle Click, Mouse 4/5, Wheel Up/Down — Left Click is deliberately left alone, so you always keep the ability to click) run independently alongside up to ten spoken words, each sending a keyboard key with full control over *how* — a single tap, held down, or repeated, for a fixed duration or until you say/press it again. Those same ten actions can also be triggered by the physical `1`–`9`/`0` keys instead of your voice — added mainly so able-bodied friends testing the app could use it without needing to talk to it.

## Installing

Grab the latest release from the [Releases page](https://github.com/Fizzil/VoicePress/releases), unzip it, and run the `.exe`. It's self-contained — no separate .NET install needed.

Windows SmartScreen will likely warn that it's from an unrecognized publisher (it's unsigned) — click **More info → Run anyway**.

Requires Windows 10/11 (64-bit) and a microphone.

## A small personal project

This is a small app built for one person's own accessibility setup — not a commercial product. It's shared here in case it helps someone else in a similar situation. See [LICENSE](LICENSE): noncommercial use only, please don't sell it.
