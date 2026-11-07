# UnboundKeys Manual

How UnboundKeys behaves. See [README.md](README.md) for what it's for and how to install it.

Everything here works with the left mouse button alone: no right-clicks, no scroll wheel, no typing. That's deliberate — a remapped right button or wheel is swallowed by UnboundKeys itself, so the app never depends on them.

## The dashboard

The dashboard is a fixed-size window with five pages down its left side — **Mouse**, **Keyboard**, **Voice**, **Steering** and **Settings** — and, in the bottom-left corner, the current **profile**, a **Listening** switch, a **Fade** switch, **Stop** and **Quit**. The Mouse page starts with a short "start here" card until you click Got it.

- Drag it by its top strip (the app's name, or the page title). It remembers where you leave it.
- At the top right of every page: the **?**, glowing in the accent colour, opens **Help**; the **—** and **✕** beside it minimize and close the dashboard. Closing only hides it: UnboundKeys keeps running in the system tray (the skull icon by the clock). Click that icon, press **Menu** on the on-screen keyboard, double-tap Caps Lock on a real keyboard, or say **"press menu"**, to bring it back. If Windows has tucked the icon behind the little **^** arrow, drag it out onto the taskbar once. The first time you close it, the tray icon says so.
- **Quit** is the last row of the rail: click it once to arm it, then again within four seconds. (It's in Settings too.)
- **Stop**, above Quit, lets go of every held or repeating key with one click.
- It never takes keyboard focus, so a game underneath keeps receiving input while you click around in it.
- **Help** is this manual in one page, one fold per topic: the voice commands, the modes, steering, the safety nets and the on-screen keyboard tricks. Open it with the **?**, or from Settings → **Help** → **Open Help**.

## Voice commands

Say **"press"** and a number, `one` through `ten`. Speech recognition runs fully offline with a vocabulary limited to just these words, so ordinary conversation won't trigger anything.

By default `one`–`nine` send the number keys and `ten` sends `0`. What each word sends, and how, is changed on the **Voice** page: click a word's tile. Under the picture, beside the keypad, **Heard** shows what the microphone just made of your words as you say them, with a command that fired in the accent colour, so you can tell a miss from a mishearing. A command fires as soon as the phrase is complete, not after the pause that follows it.

Three commands always work, whatever's mapped:

| Say | Effect |
|---|---|
| **"press stop"** | releases every key currently held or repeating |
| **"press menu"** | shows the dashboard, or hides it again |
| **"press fade"** | turns Fade on or off |

## Mouse keys

**Right Button**, **Middle Button**, **Mouse 4**, **Mouse 5**, **Wheel Up** and **Wheel Down** can each send a key too. On the **Mouse** page, click a button on the drawn mouse — or its row — to change it. A button starts unmapped (a normal click) until you assign it a key; once mapped, pressing it sends the key instead of the usual click. (A mapping can send a click as well: the key picker's Mouse group has Left Click, Right Click, Middle Click and Mouse Button 4 and 5.)

**One button, two things.** At the bottom of a mouse button's editor is a **Double press** switch, off to begin with. Switch it on and **Single press** and **Double press** appear at the top of the editor, with the double press open to be set up; switch it off and the double press is cleared. On the Mouse page, a button with a double press shows a **D** beside its key; hover it to see what the double press sends. Each is a complete mapping of its own — keys, Mode, Infinite and all — started by its own way of pressing and stopped by the same way again: single press to start the first, single press to stop it; double press for the second. One button does one thing at a time: starting one stops the other, so a single press while the double press's mapping is holding a key switches to the single press's. With a double press set, a single press lands a moment late (0.3 s), once it's clear no second press is coming, and the button's ordinary click no longer passes through even while its Single press is Not Mapped. The keys on the Keyboard page have the same two, for the real key and the on-screen one alike.

Left Click is never remappable: it's the one button that always works as a click, so no mapping can ever lock you out of the app.

## Steering

For racing games, with a mouse that is really a joystick (a lip- or chin-operated one, say): the mouse becomes a steering wheel with a rubber band.

- **The band.** While steering is on, the pointer is held near the middle of the screen. Moving the mouse moves it as usual, but never past a line on any side (the **Stretch**), and the moment you stop it springs back to the centre.
- **The controller.** UnboundKeys makes a **Virtual Xbox controller** of its own and tells it where the pointer sits. Sideways is the steering: the further out, the harder the turn, with full lock at the stretch line. Up and down drive nothing on the controller; they only press **Move up** and **Move down** (below).
- **The pedals** are keys you map like any other: the key picker's **Controller** group has **Throttle** and **Brake**, for any word, mouse button, keyboard key or move. They work with steering off too: while a pedal is held, the controller plugs itself in.
- **Starts and stops steering** is a switch at the bottom of every mapping's editor (above Double press, where there is one), off to begin with. Switch it on and that word, button or key turns steering on and off. While the switch is on, the mapping sends only Throttle or Brake; any other key on it is not sent, so "press nine" with the switch on no longer types a 9. The Steering page says whether steering is on and which words, buttons or keys switch it.
- **Default**, on the Steering page, is a ready-made racing setup in the active profile. **Middle Button** sends Throttle with **Hold**, **Infinite** and Starts and stops steering, so one click starts the race (steering on, foot down) and the next click ends it. **Move down** sends Brake as a one-second **Hold**: moving down brakes for up to a second, and lets go early when the pointer comes back. Then the Mouse page opens. Change any of it at any time; or, under Default, **Mouse**, **Keyboard** and **Voice** take you to the pages where you can choose your own switch.
- **Move up and Move down** (the Steering page's **Move mouse** rows) are mappings of their own, set like a mouse button's. While steering is on, one is pressed when the pointer goes past a third of the stretch, up or down, and let go when it comes back. Letting go stops the mapping: a Hold or Repeat with Infinite lasts exactly as long as the move, a timed Hold lets go early, and a Tap taps once per move.
- **One Infinite hold at a time.** Move up and Move down count as mouse buttons for the one-hold rule (see Safety nets): an Infinite hold on a move stops an Infinite hold on a mouse button, such as Default's throttle. So a pedal on a move should be a timed Hold, not Infinite.
- **Stretch** — how far the pointer can go from the centre, as a share of the way to the screen's edge: 10 % to start, from 5 % to 100 % in steps of 5 % (**−5 %**, **+5 %**), and **Reset** goes back to 10 %.
- **The controller line**, at the bottom of the Steering page, reads "Virtual Xbox controller: ready." once the free **ViGEmBus** driver is installed. Until then it says the driver is not installed and shows a **Get the ViGEmBus driver** button, which opens its download page (github.com/nefarius/ViGEmBus) in your browser; it is installed once. Without the driver the band still works, but nothing drives the car.
- **Switching it off.** Steering is off at every start. **Stop** and the other safety nets switch it off, and so does another window coming in front of the game. The band lets the pointer go while the dashboard is the window in front, and while the left button is held (the mouse's own click-and-hold), so you can always drag the pointer out and click something.
- **The same in every profile.** The stretch, Move up and Move down are the app's, so you can stay in your everyday profile and just say the word; only which word, button or key starts steering is set per profile. Reset profile leaves Move up and Move down alone; Reset app clears them.

## Editing a mapping

Clicking a mouse button, a spoken word or a keyboard key opens its editor page (**‹ Back** returns to the list).

- **Keys** — Key 1 is what it sends. **+ Add key** adds more, up to six in all; the **✕** beside an extra key removes it (click it once to arm it, then again). Clicking a key's value opens a picker: click a category on the left, then a key on the right — hover near the top or bottom edge of a long list to scroll it.
- **Mode** — **Tap** presses the keys together, once (the default). **Repeat** presses them one at a time, again and again. **Rotation** presses all of them in priority order every tick, so the game takes the first that's ready (see below). **Hold** keeps them pressed, repeating like a key held on a real keyboard.
- **Duration** — for Repeat, Rotation and Hold: how long, in steps of +0.1 s and +1 s, or **Reset** to 0. **Infinite** instead keeps pressing the keys repeatedly (or holding them) until you say the word (or press the button) again; turning Infinite on clears the duration, and changing the duration turns Infinite off.
- **Infinite pause** — below Infinite, and one or the other with it: switching one on switches the other off, and **Tap** clears both. It is for a key that must cut into an infinite repeat: switch it on and set the time (1.0 s to start, in steps of 0.1 s and never under 0.1 s, **Reset** back to 1.0 s; one to two seconds usually does it, and a channelled ability wants its cast time). Press the key and every infinite repeat pauses that long while it fires, then resumes where it left off. Then set the key's Mode to **Repeat** or **Hold** for the same time, so it actually presses the key during the pause: **Repeat** for an instant ability, **Hold** for a cast or a channel (Hold needs the game's press-and-hold casting on). Two such keys in a row extend the pause, and a timed repeat pauses the same way.
- **Starts and stops steering** — makes this word, button or key the steering switch (see Steering).
- **Reset this mapping** puts it back to its default; on a mouse button or a keyboard key with Double press on, **Reset both** beside it resets the single press and the double press together. Both ask "Are you sure?" first; click again to confirm. To reset every mapping at once, use Settings → **Reset profile**.

Saying a word (or pressing a button) again while its Repeat or Hold is still running — Infinite or not — stops it early. That makes a long fixed duration a usable stand-in for Infinite whenever you'd rather have a backstop maximum length.

With several keys: **Tap** and **Hold** press them all together, as a combo like Ctrl+C (while a Hold lasts, its last key repeats, as on a real keyboard). **Repeat** cycles through them one at a time — Key 1, then Key 2, and so on, then back to Key 1 — a tenth of a second apart. **Rotation** presses all of them in order every tick, a few milliseconds apart, so the game takes the first one that's ready and rejects the rest: a one-button rotation in your own priority order, Key 1 first, with the game deciding what's ready and no cooldown numbers to enter. Leave anything you want to press yourself off the list, and turn the game's error speech down, since the rejected presses say "not ready". Duration, Infinite and Infinite pause work for Rotation as they do for Repeat.

## The on-screen keyboard

The **Keyboard** page shows a map of the keyboard: the lit keys — the digits and letters — are the ones you can remap; click one to edit it. A key that's been changed is lit in the theme colour (hover it to see how it's pressed), and the same key on a real keyboard is caught too. Everything else on the keyboard (Tab, Enter, Shift, the arrows…) is a plain key.

**Show on-screen keyboard** opens the keyboard itself — a floating window that stays on top and never takes focus, so it types into whatever is behind it. If it was open when you last quit, it comes back on the next start where you left it, in the same layout, and the dashboard stays hidden until you press Menu or click the tray icon.

- Click a key to press it; hold it to repeat. Its letter and number keys send whatever they've been remapped to.
- **Shift, Ctrl, Alt, Win** are sticky: click one (it lights up), then click the key it should combine with — everything lets go together. Click a lit modifier again to cancel it.
- **Caps** stays lit while Caps Lock is on, and the letters show as capitals. Two quick clicks of Caps is the panic button: it releases everything and lifts Fade.
- **Word suggestions** appear above the keys as you type; click one to type the rest of the word plus a space. With **Smart predictive text** switched on (Keyboard page; it is off by default), the words you type are kept on this PC and float to the top over time; beside it, **Open the list** shows them in a text editor, where you can change them or add your own (one word per line), and **Clear the list** forgets them all. While it is off, nothing you type is kept. Like Windows' own on-screen keyboard, it loses track of the word if you click somewhere else or use the arrow keys, and picks up again at your next word.
- **Mini** collapses it to a single strip of the essential keys; **Maxi** brings it back. **Fade** dims it along with the dashboard. **Menu** shows the dashboard, or hides it again, no microphone or taskbar needed. Those keys and the drag handle on the right edge sit in the same corner in both layouts, so nothing moves out from under your mouse.
- Drag it by the handle on its right edge. It remembers its position and its Mini state.
- **Keyboard size** (on the Keyboard page) is Small, Medium or Large; Small is about the size of Windows' own on-screen keyboard.
- **Key click sound** (on the Keyboard page) gives every key of the on-screen keyboard and the rename keyboard a soft click; switch it off there to type silently.

## Fade

Fade dims the dashboard and the on-screen keyboard to 20% so you can see the game through them; they stay clickable. Turn it on or off from the dashboard's Fade switch, the keyboard's Fade key, or by saying "press fade". The panic taps (two quick clicks of the keyboard's Caps, or a double-tap of a real Caps Lock) lift it too.

## Listening

The **Listening** switch pauses the voice keys only. With it off, nothing you say does anything, and whatever a spoken word was holding or repeating is released; if a spoken word is the steering switch, steering is switched off too. Mouse remaps, the on-screen keyboard and remapped keys on a real keyboard keep working, so you can mute the voice keys in a menu or a chat without losing your mouse buttons. The tray icon dims while paused; switch it back on to resume.

## Safety nets

Because Infinite holds and repeats can run indefinitely, UnboundKeys is built so a stuck key is never the only way out:

- Voice keys, mouse keys and keyboard keys each get their own slots: only one *word*, one *mouse button* and one *keyboard key* can be doing an Infinite hold at a time (and likewise an Infinite repeat). Starting a new one stops whichever of the same kind had that slot, never the other kinds: that is how "run backward" stops "run forward". Move up and Move down count as mouse buttons here. Infinite repeats of different kinds run side by side, each on its own timer, without queueing or blocking one another.
- **Stop** on the rail, **"press stop"**, two quick clicks of the on-screen keyboard's **Caps**, or a double-tap of a real **Caps Lock** key releases everything currently held or repeating and switches steering off, all at once, no matter what's mapped. Either Caps double-tap also shows the dashboard, or hides it if it was showing. Caps Lock's own on/off state ends up right back where it started, since two real toggles cancel out.
- Quitting UnboundKeys releases everything too. Switching away from the game window (alt-tab, or clicking another window) lets go of every Infinite hold and repeat and switches steering off; switching browser tabs lets go of the holds and repeats as well. A timed Hold or Repeat runs out its own time (or say the word again, or Stop). A hold made only of Throttle and Brake is kept when just the browser's title changes, so a browser game can't lift the throttle by renaming its tab; a real switch of window still lets go of it. Pausing Listening, or losing the microphone, releases whatever the voice keys were holding.
- The on-screen keyboard's sticky modifiers are released whenever the keyboard closes, so a Shift or Ctrl can't be left held down.

## Profiles

A profile is a game. Each one has its own colour theme and up to ten **sub-profiles** (a class, a loadout, a character), and each sub-profile is a complete set of mappings: the ten words, the six mouse buttons, the keyboard keys. Switching sub-profiles swaps everything over at once; switching profiles also brings the colour with it, and lands on whichever sub-profile that game was last on. Up to ten profiles.

- Switch from the **profile chip** in the bottom-left corner of the dashboard: the flyout lists the profiles, folded. Click a profile to show its sub-profiles, then click a sub-profile to switch to it (the one in use says Active). **Manage profiles…**, at the bottom, opens Settings with the PROFILES section open. Or from Settings: click the PROFILES heading to open the list, click a profile to show its sub-profiles, and click a sub-profile to switch to it, even one under another profile.
- **Add Profile** (in Settings) creates a fresh game named "Profile 1", "Profile 2"... with one sub-profile, Default: the words at their default keys and the mouse buttons unmapped.
- **Add sub-profile**, under a profile in Settings, starts as a copy of the sub-profile that profile is on, so a second class only needs the few keys that differ changed. It becomes active straight away.
- **Rename** opens an on-screen keyboard: type the name with the mouse, then **Done**. The key under the pointer lights up, and a key held down repeats, so holding ⌫ clears a name quickly. Nothing is capitalized for you: click **Shift** for a capital (it stays lit for the next key you type, then lets go), or **Caps** to lock capitals. Enter is Done, Esc is Cancel and Del clears the name. Profiles and sub-profiles rename the same way.
- The **✕** deletes a profile or a sub-profile: click it once to arm, then again. Deleting the active profile switches you to Default; deleting the active sub-profile moves you to the first remaining one. A profile always keeps at least one sub-profile.
- **Default** always exists and cannot be renamed or deleted.

## Settings

Settings has nine sections: **Theme**, **Profiles**, **Startup**, **Updates**, **Action logger**, **Reset**, **Quit**, **Help** and **About**. Each folds under its heading and, while closed, shows a one-line summary of its state; click the heading to open it.

- **Theme** — Red, Green or Blue; the choice is saved with the current profile.
- **Profiles** — as above; the heading folds the list away to save room, click it to open.
- **Startup** — **Start with Windows** starts UnboundKeys when you sign in, through a scheduled task that runs it as administrator without the permission prompt. **Start with voice keys paused** applies to that automatic start only: the app comes up with Listening off until you flip it on. The task is refreshed each time the app starts, so it keeps pointing at the copy you last ran, updates included.
- **Updates** — **Check for updates** asks Fizzil's GitHub for the newest release, only when you click it and confirm. If a newer version exists, the same card can download and install it: the new version unpacks into a folder beside the current one, starts, and this one quits. The download is checked against the SHA-256 fingerprint GitHub lists for it, and one that does not match is thrown away rather than installed. Profiles and mappings carry over; the old folder stays until you delete it. A shortcut you made to UnboundKeys (on the desktop, in the Start menu or pinned to the taskbar) is pointed at the newest version each time the app starts, so it keeps opening the version you last ran.
- **Action logger** — off by default; see Saved data below. **Open log folder** shows the log file.
- **Reset profile** — every mapping in the active profile's current sub-profile back to its default (Steering's Move up and Move down are the app's, and stay). The button asks "Are you sure?"; click it again to confirm.
- **Reset app** — deletes everything the app keeps (every profile and mapping, the settings and their backup, the kept words, the log and the Start with Windows task) and starts the app again as a fresh install. It asks "Are you sure?" first, and it cannot be undone.
- **Quit** — completely shuts down UnboundKeys and shuts down all key mappings (two clicks).
- **Help** — **Open Help** opens the Help page, the same as the **?** at the top right.
- **About** — the version, and the credit for the word suggestions' list.

## Saved data

Everything is saved to `%AppData%\UnboundKeys\settings.json` — profiles and mappings, the active profile and theme, Steering's stretch and its Move up and Move down, the dashboard's and keyboard's positions and the keyboard's size — and reloaded next launch. Each save writes a new file and swaps it in, keeping the previous one as `settings.json.bak`; if `settings.json` is ever unreadable, the backup is used instead, and the unreadable file is kept as `settings.json.unreadable`. If you switch on **Smart predictive text**, the keyboard's learned words are in `%AppData%\UnboundKeys\learned-words.txt`, until you clear the list.

UnboundKeys keeps a small log, `%AppData%\UnboundKeys\log.txt`. Errors and crashes are always written there, logger on or off, so a fault can be found afterwards. The **action logger**, used for debugging and for building new features, adds the rest: it is **off** unless you switch it on in Settings → **Action logger**, and while it is on the app also notes its starts, stops and the changes you make in the dashboard (a mapping's keys or mode, the profile). Nothing you type or say is ever written there. Past 256 KB the file starts again, keeping the previous one as `log.txt.old`. If something goes wrong, switch it on, make it happen again, and that file (**Open log folder**) is the first thing to look at, or to send along.

## Troubleshooting

**Windows asks for permission every time UnboundKeys starts.** That's expected: it runs as administrator so its key presses reach games that run elevated, and so it can see their input at all. Click **Yes**.

**A mapped mouse button or keyboard key does nothing in a specific game — it still reacts to the real click or key instead of the mapped one — even though voice keys work there.** The game's anti-cheat (EasyAntiCheat, BattlEye, Vanguard…) may be blocking the kind of low-level input hook UnboundKeys uses, since it's the same technique some cheat tools use. That isn't something UnboundKeys can work around.

**UnboundKeys says it couldn't start speech recognition.** Make sure a microphone is connected and chosen as the default input under Windows Settings → System → Sound, then start it again.

**The microphone was unplugged or stopped.** UnboundKeys notices: it lets go of whatever the spoken words were holding (and switches steering off, if a word is its switch), the tray icon dims and says "microphone lost, trying again", and it tries the microphone again every few seconds until it is back.

**The tray icon isn't visible.** Windows hides new tray icons behind the **^** arrow by the clock; click it and drag the skull out onto the taskbar once. The keyboard's Menu key, or saying "press menu", brings the dashboard back regardless.
