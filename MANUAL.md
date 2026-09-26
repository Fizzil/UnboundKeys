# UnboundKeys Manual

How UnboundKeys behaves. See [README.md](README.md) for what it's for and how to install it.

Everything here works with the left mouse button alone: no right-clicks, no scroll wheel, no typing. That's deliberate — a remapped right button or wheel is swallowed by UnboundKeys itself, so the app never depends on them.

## The dashboard

The dashboard is a fixed-size window with five pages down its left side — **Mouse**, **Keyboard**, **Voice**, **Settings** and **Help** — and, in the bottom-left corner, the current **profile**, a **Listening** switch and a **Fade** switch.

- Drag it by its top strip (the app's name, or the page title). It remembers where you leave it.
- The **—** and **✕** at the top right minimize and close it. Closing only hides it: UnboundKeys keeps running in the system tray (the skull icon by the clock). Click that icon, press **Menu** on the on-screen keyboard, double-tap Caps Lock on a real keyboard, or say **"press menu"**, to bring it back. If Windows has tucked the icon behind the little **^** arrow, drag it out onto the taskbar once.
- It never takes keyboard focus, so a game underneath keeps receiving input while you click around in it.
- **Quit** is at the bottom of Settings — two clicks, to avoid accidents.
- **Help** is this manual in one page: the voice commands, the modes, the safety nets and the on-screen keyboard tricks.

## Voice commands

Say **"press"** and a number, `one` through `ten`. Speech recognition runs fully offline with a vocabulary limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` send the number keys and `ten` sends `0`. What each word sends, and how, is changed on the **Voice** page: click a word's tile.

Three commands always work, whatever's mapped:

| Say | Effect |
|---|---|
| **"press stop"** | releases every key currently held or repeating |
| **"press menu"** | brings the dashboard back |
| **"press fade"** | turns Fade on or off |

## Mouse keys

Right Click, Middle Click, Mouse 4, Mouse 5, Wheel Up and Wheel Down can each send a key too. On the **Mouse** page, click a button on the drawn mouse — or its row — to change it. A button starts unmapped (a normal click) until you assign it a key; once mapped, pressing it sends the key instead of the usual click.

Left Click is never remappable: it's the one button that always works as a click, so no mapping can ever lock you out of the app.

## Editing a mapping

Clicking a mouse button, a spoken word or a keyboard key opens its editor page (**‹ Back** returns to the list).

- **Keys** — Key 1 is what it sends. **+ Add key** adds more, up to six in all; the **✕** beside an extra key removes it (click it once to arm it, then again). Clicking a key's value opens a picker: hover a category on the left, then click a key on the right — hover near the top or bottom edge of a long list to scroll it.
- **Mode** — **Tap** presses the keys once (the default). **Repeat** taps them again and again. **Hold** keeps them pressed.
- **Duration** — for Repeat and Hold: how long, in steps of +0.1 s and +1 s, or **Reset** to 0. **Infinite** instead runs until you say the word (or press the button) again; turning Infinite on clears the duration, and changing the duration turns Infinite off.
- **Custom gaps between keys** — for Repeat with two or more keys: how long to wait after each key before the next one, one row per key. A gap left at *default* is the usual 0.1 s.
- **Reset this mapping** puts it back to its default. Tapping Reset three times quickly also reveals **Reset every mapping**, the same as Settings → Reset All.

Saying a word (or pressing a button) again while its Repeat or Hold is still running — Infinite or not — stops it early. That makes a long fixed duration a usable stand-in for Infinite whenever you'd rather have a backstop maximum length.

With several keys: **Tap** and **Hold** press them all together, as a combo like Ctrl+C. **Repeat** cycles through them one at a time — Key 1, then Key 2, and so on, then back to Key 1 — with the default gap between each unless you've set custom gaps.

## The on-screen keyboard

The **Keyboard** page shows a map of the keyboard: the lit keys — the digits and letters — are the ones you can remap; click one to edit it. A key that's been changed shows what it now sends, and the same key on a real keyboard is caught too. Everything else on the keyboard (Tab, Enter, Shift, the arrows…) is a plain key.

**Show on-screen keyboard** opens the keyboard itself — a floating window that stays on top and never takes focus, so it types into whatever is behind it.

- Click a key to press it; hold it to repeat. Its letter and number keys send whatever they've been remapped to.
- **Shift, Ctrl, Alt, Win** are sticky: click one (it lights up), then click the key it should combine with — everything lets go together. Click a lit modifier again to cancel it.
- **Caps** stays lit while Caps Lock is on, and the letters show as capitals. Two quick clicks of Caps is the panic button: it releases everything and lifts Fade.
- **Word suggestions** appear above the keys as you type; click one to type the rest of the word plus a space. Words you type are remembered and float to the top over time. Like Windows' own on-screen keyboard, it loses track of the word if you click somewhere else or use the arrow keys, and picks up again at your next word.
- **Mini** collapses it to a single strip of the essential keys; **Maxi** brings it back. **Fade** dims it along with the dashboard. **Menu** shows the dashboard, or hides it again, no microphone or taskbar needed. Those keys and the drag grip on the right edge sit in the same corner in both layouts, so nothing moves out from under your mouse.
- Drag it by the grip on its right edge. It remembers its position and its Mini state.
- **Keyboard size** (on the Keyboard page) is Small, Medium or Large; Small is about the size of Windows' own on-screen keyboard.

## Fade

Fade dims the dashboard and the on-screen keyboard to 20% so you can see the game through them; they stay clickable. Turn it on or off from the dashboard's Fade switch, the keyboard's Fade key, or by saying "press fade". The panic taps (two quick clicks of the keyboard's Caps, or a double-tap of a real Caps Lock) lift it too.

## Listening

The **Listening** switch pauses the voice keys only. With it off, nothing you say does anything, and whatever a spoken word was holding or repeating is released. Mouse remaps, the on-screen keyboard and remapped keys on a real keyboard keep working, so you can mute the voice keys in a menu or a chat without losing your mouse buttons. The tray icon dims while paused; switch it back on to resume.

## Safety nets

Because Infinite holds and repeats can run indefinitely, UnboundKeys is built so a stuck key is never the only way out:

- Voice keys, mouse keys and the on-screen keyboard each get their own slots: only one *word*, one *mouse button* and one *keyboard key* can be doing an infinite hold at a time (and likewise an infinite repeat) — starting a new one only bumps whichever of the same kind had that slot, never the other kinds. Infinite repeats of different kinds run side by side, each on its own timer, without queueing or blocking one another.
- **"Press stop"**, two quick clicks of the on-screen keyboard's **Caps**, or a double-tap of a real **Caps Lock** key releases everything currently held or repeating, all at once, no matter what's mapped. The real Caps Lock double-tap also shows the dashboard, or hides it if it was showing. Caps Lock's own on/off state ends up right back where it started, since two real toggles cancel out.
- Quitting UnboundKeys, or switching away from the game window (alt-tab, or switching browser tabs), releases everything automatically too. Pausing Listening releases whatever the voice keys were holding.
- The on-screen keyboard's sticky modifiers are released whenever the keyboard closes, so a Shift or Ctrl can't be left held down.

## Profiles

A profile is a game. Each one has its own colour theme and up to ten **sub-profiles** (a class, a loadout, a character), and each sub-profile is a complete set of mappings: the ten words, the six mouse buttons, the keyboard keys. Switching sub-profiles swaps everything over at once; switching profiles also brings the colour with it, and lands on whichever sub-profile that game was last on. Up to ten profiles.

- Switch from the **profile chip** in the bottom-left corner of the dashboard: the flyout lists the profiles and, under the active one, its sub-profiles. Or from Settings: click the PROFILES heading to open the list, click a profile to show its sub-profiles, and click a sub-profile to switch to it, even one under another profile.
- **Add Profile** (in Settings) creates a fresh game named "Profile 1", "Profile 2"... with one sub-profile, Default: the words at their default keys and the mouse buttons unmapped.
- **Add sub-profile**, under a profile in Settings, starts as a copy of the sub-profile that profile is on, so a second class only needs the few keys that differ changed. It becomes active straight away.
- **Rename** opens an on-screen keyboard: type the name with the mouse, then **Done**. The first letter of each word comes out capitalized on its own (Shift lights up at a word start; click it off if you want lowercase), and Caps locks capitals. Profiles and sub-profiles rename the same way.
- The **✕** deletes a profile or a sub-profile: click it once to arm, then again. Deleting the active profile switches you to Default; deleting the active sub-profile moves you to the first remaining one. A profile always keeps at least one sub-profile.
- **Default** always exists and cannot be renamed or deleted.

## Settings

- **Theme** — Red, Green or Blue; the choice is saved with the current profile.
- **Profiles** — as above; the heading folds the list away to save room, click it to open.
- **Start with Windows** — starts UnboundKeys when you sign in, through a scheduled task that runs it as administrator without the permission prompt. **Start with voice keys paused** applies to that automatic start only: the app comes up with Listening off until you flip it on. The task is refreshed each time the app starts, so it keeps pointing at the copy you last ran, updates included.
- **Check for updates** — asks Fizzil's GitHub for the newest release, only when you click it and confirm. If a newer version exists, the same card can download and install it: the new version unpacks into a folder beside the current one, starts, and this one quits. Profiles and mappings carry over; the old folder stays until you delete it.
- **Reset All** — every mapping in the current profile back to its default (two clicks).
- **Quit** — stops UnboundKeys completely (two clicks), releasing anything held.

## Saved data

Everything is saved to `%AppData%\UnboundKeys\settings.json` — profiles and mappings, the active profile and theme, the dashboard's and keyboard's positions and the keyboard's size — and reloaded next launch. The keyboard's learned words are in `%AppData%\UnboundKeys\learned-words.txt`.

## Troubleshooting

**Windows asks for permission every time UnboundKeys starts.** That's expected: it runs as administrator so its key presses reach games that run elevated, and so it can see their input at all. Click **Yes**.

**A mapped mouse button or keyboard key does nothing in a specific game — it still reacts to the real click or key instead of the mapped one — even though voice keys work there.** The game's anti-cheat (EasyAntiCheat, BattlEye, Vanguard…) may be blocking the kind of low-level input hook UnboundKeys uses, since it's the same technique some cheat tools use. That isn't something UnboundKeys can work around.

**UnboundKeys says it couldn't start speech recognition.** Make sure a microphone is connected and chosen as the default input under Windows Settings → System → Sound, then start it again.

**The tray icon isn't visible.** Windows hides new tray icons behind the **^** arrow by the clock; click it and drag the skull out onto the taskbar once. The keyboard's Menu key, or saying "press menu", brings the dashboard back regardless.
