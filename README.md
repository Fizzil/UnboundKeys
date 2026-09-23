# UnboundKeys

Remap mouse buttons and voice commands to keyboard keys, with full control over how each one gets pressed — a small Windows overlay built for playing **The Isle: Evrima**.

## Screenshots

<p float="left">
  <img src="Assets/screenshots/UBK-Voice-Keys.png" width="30%" alt="The Voice Keys tab open, showing word 1's remap card with a Ctrl+X combo, Repeat, and the K1/K2 timing row" />
  <img src="Assets/screenshots/UBK-Mouse-Keys.png" width="30%" alt="The Mouse Keys tab open, showing the six remappable mouse buttons" />
  <img src="Assets/screenshots/UBK-Profile.png" width="30%" alt="The Profile tab, showing Default and a custom 'the isle' profile" />
</p>

<img src="Assets/screenshots/UBK-Keyboard.png" width="100%" alt="The full-size on-screen keyboard, Keyboard/Mouse Keys/Voice Keys/Profile/Fade row above it" />
<img src="Assets/screenshots/UBK-Keyboard-Mini.png" width="100%" alt="The keyboard collapsed down to its minimized quick-access strip" />
<img src="Assets/screenshots/UBK-RGB-Color-Themes.png" width="100%" alt="The Red/Green/Blue color theme picker" />

## What it does

- Say "press one" through "press ten" to send the matching key, or click a key on the built-in on-screen keyboard — a full mouse-clickable keyboard for typing and remapping without any physical keyboard at all.
- Remap Right Click, Middle Click, Mouse 4/5, and Wheel Up/Down to send a key too — Left Click is left alone on purpose, so you can never lose the ability to click.
- Remap which key each word or button sends — each one taps its key by default, or can be set to hold or repeat instead — and manage multiple named profiles, left-click the skull icon for a small dashboard.
- Add up to two more keys to a word or button for a combo, like Ctrl+C — tap and hold press them all together, and repeat cycles through them in sequence, with the timing between each adjustable.
- Say "press stop," or double-tap the physical Caps Lock key, to release anything currently held down or repeating — a safety net for whenever a command can't be repeated in time. Never interferes with Caps Lock's normal function (its on/off state ends up right back where it started, since two real toggles cancel out).
- Say the word (or press the button) again while it's mid-hold or mid-repeat to stop it early, instead of waiting out its full duration — handy for setting a long duration as a stand-in for indefinite.
- Right-click the skull icon to pause listening, or drag it to move it around the screen.
- Runs fully offline — speech recognition ([Vosk](https://alphacephei.com/vosk/)) happens entirely on your machine. Nothing is sent anywhere.

See [MANUAL.md](MANUAL.md) for the full details — exact voice commands, safety nets, profile behavior, and everything the dashboard does.

## Why this exists

UnboundKeys was built as an accessibility tool, for Fizzil — who is disabled — to be able to play *The Isle: Evrima*, using a normal mouse or a specialized **quad mouse** (a mouse with extra physical buttons). It is **not** intended as a way to play the game hands-free with voice alone.

It's a mouse-button and voice remapper in one: six of a mouse's buttons (Right Click, Middle Click, Mouse 4/5, Wheel Up/Down — Left Click is deliberately left alone, so you always keep the ability to click) run independently alongside up to ten spoken words, each sending a keyboard key with full control over *how* — a single tap, held down, or repeated, for a fixed duration or until you say/press it again.

## Installing

Grab the latest release from the [Releases page](https://github.com/Fizzil/UnboundKeys/releases), unzip it, and run the `.exe`. It's self-contained — no separate .NET install needed.

Windows SmartScreen will likely warn that it's from an unrecognized publisher (it's unsigned) — click **More info → Run anyway**.

Requires Windows 10/11 (64-bit) and a microphone.

## A small personal project

This is a small app built for one person's own accessibility setup — not a commercial product. It's shared here in case it helps someone else in a similar situation. See [LICENSE](LICENSE): noncommercial use only, please don't sell it.
