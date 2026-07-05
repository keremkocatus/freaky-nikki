# Usage guide

## Install

Download from the [Releases](../../../releases) page:

- **`FreakyNikki-win-Setup.exe`** (recommended) — installs to your user profile
  (no admin), and auto-updates itself afterwards.
- **`FreakyNikki-win-Portable.zip`** — unzip and run `FreakyNikki.exe`. No install,
  but no auto-update either.

Both are self-contained; you don't need the .NET runtime.

## Basic flow

1. Set your **default** playback device in Windows as usual — that one always plays
   and is shown locked in the list as *Default · playing*.
2. Tick any **extra** output devices you want the sound mirrored to.
3. Press **Start**.
4. Adjust each device's **Volume** and **Delay** as needed.

Your selections and per-device settings are remembered between runs.

## Tuning delay

Bluetooth devices lag behind wired ones (typically ~100–250 ms, and it differs per
device), so mirrored audio can echo. Each extra device has a **Delay** control to
compensate:

- New Bluetooth devices are pre-filled with **~150 ms**; wired devices start at 0.
- Drag the slider, or click the **ms** field and type an exact value (0–500),
  then press Enter.
- Rule of thumb: the device you hear **earlier** needs **more** delay. Nudge until
  the echo collapses into a single sound.

The default device is your reference point (0 ms) — you're pushing the extra
devices back to meet it.

## Tray & window

- Closing the window (**X**) hides Freaky Nikki to the **system tray**; it keeps
  running.
- Tray menu: **Open**, **Start/Stop**, **Quit**. Double-click the tray icon to open.
- Only one instance runs at a time; launching it again just re-opens the window.

## Long sessions (movies, series)

Freaky Nikki is built to survive multi-hour playback:

- It keeps devices fed during quiet passages (no dropouts when a scene goes silent).
- It corrects slow clock drift so devices don't gradually desync.
- It automatically recovers after the PC sleeps/resumes, or if a Bluetooth device
  stalls or briefly drops — audio comes back on its own within a few seconds.

If a device ever gets stuck, toggling its checkbox off/on (or Stop/Start) forces a
clean restart.

## Troubleshooting

**I hear an echo / the devices are out of sync.**
Increase the **Delay** on the device you hear first. See [Tuning delay](#tuning-delay).

**A Bluetooth device stutters or crackles.**
A single Bluetooth adapter streaming two devices at once can overload on weaker
chipsets. Try one Bluetooth + one wired, or a better adapter.

**No sound on the extra device.**
Check the status dot: red means the device couldn't be opened (in exclusive use by
another app, or access denied). Green means it's playing. Make sure the device
isn't set to *exclusive mode* by another application.

**A device disappeared from the list.**
Only *active* devices are listed. Unplugged/disabled devices reappear (and resume,
if they were enabled) when they come back.

**Something else.**
Check the log at `%AppData%\FreakyNikki\log.txt` and open an
[issue](../../../issues) with it.

## Uninstall

If you used the installer: uninstall from **Settings → Apps** like any other app.
Portable: just delete the folder. Either way you can remove leftover settings at
`%AppData%\FreakyNikki`.
