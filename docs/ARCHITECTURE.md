# Architecture

How Freaky Nikki mirrors your system audio to extra devices, end to end.

## The core idea

Windows already plays your audio on the **default** output device. Freaky Nikki
never touches that path — your main headphones keep playing with **zero added
latency**. Instead it grabs a *copy* of the system mix with **WASAPI loopback
capture** and pushes that copy to each extra device you enable.

```
System audio (Spotify, YouTube, a game…)
        │
        ▼
Default output device (Headphone 1) ── plays natively, untouched
        │
        ▼
WASAPI loopback capture  ── a copy of the same mix
        │
        ├─► OutputChannel ─► Headphone 2
        └─► OutputChannel ─► Speaker N
```

Because each extra device (especially Bluetooth) adds its own latency, every
channel has a **per-device delay** so you can line them up by ear.

Why loopback instead of a virtual audio driver? A virtual device (VB-Cable style)
needs a signed kernel driver and per-device setup. WASAPI loopback runs entirely
in user mode — no driver, no admin, no reboot — which is the only realistic
choice for a small, portable OSS tool.

## Components

| Component | File | Responsibility |
|---|---|---|
| `AudioEngine` | [Audio/AudioEngine.cs](../src/FreakyNikki/Audio/AudioEngine.cs) | Owns loopback capture, fans buffers out to channels, orchestrates start/stop, reconnection and recovery. |
| `OutputChannel` | [Audio/OutputChannel.cs](../src/FreakyNikki/Audio/OutputChannel.cs) | One extra device: the full signal chain into a `WasapiOut`. |
| `DelaySampleProvider` | [Audio/DelaySampleProvider.cs](../src/FreakyNikki/Audio/DelaySampleProvider.cs) | Adjustable delay via leading silence; also the "silence pump". |
| `DriftSampleProvider` | [Audio/DriftSampleProvider.cs](../src/FreakyNikki/Audio/DriftSampleProvider.cs) | Bounds latency growth from clock drift. |
| `DeviceMonitor` | [Audio/DeviceMonitor.cs](../src/FreakyNikki/Audio/DeviceMonitor.cs) | Enumerates render devices; watches hot-plug and default changes. |
| `SettingsStore` | [Settings/SettingsStore.cs](../src/FreakyNikki/Settings/SettingsStore.cs) | Atomic JSON persistence under `%AppData%\FreakyNikki`. |
| `ThemeManager` | [Theme/ThemeManager.cs](../src/FreakyNikki/Theme/ThemeManager.cs) | Follows the Windows light/dark theme and accent, live. |
| `UpdateService` | [Update/UpdateService.cs](../src/FreakyNikki/Update/UpdateService.cs) | Background GitHub update check + apply/restart (Velopack). |
| `MainViewModel` | [ViewModels/MainViewModel.cs](../src/FreakyNikki/ViewModels/MainViewModel.cs) | MVVM glue; marshals background events to the UI thread. |

## The per-device signal chain

Every enabled device gets one `OutputChannel`. The captured audio is written into
a ring buffer; a dedicated WASAPI render thread pulls it back out through:

```
BufferedWaveProvider          capture format, DiscardOnBufferOverflow, ReadFully
   │
DriftSampleProvider           trims backlog when it exceeds the ceiling
   │
channel match (mono/stereo)   only if the device layout differs
   │
WdlResamplingSampleProvider   only if the sample rate differs (44.1k ↔ 48k)
   │
VolumeSampleProvider          per-device volume
   │
DelaySampleProvider           per-device delay + silence pad
   │
WasapiOut (shared mode)       ~150 ms latency, event-driven
```

The chain is built to match the device's **shared-mode mix format exactly**
(float, its sample rate, its channel count) so WASAPI performs no format
conversion of its own and never rejects the format.

### Delay

The delay is realised as **leading silence**. Increasing it prepends more silence
(the device lags further behind); decreasing it drops already-buffered samples to
catch up. Both are safe while playing. Bluetooth devices are pre-filled with a
~150 ms default on first sight; wired devices start at 0. See
[USAGE.md](USAGE.md#tuning-delay) for how to tune it.

### Silence pump

`DelaySampleProvider.Read` always returns the full requested buffer, padding with
silence when the source is empty. That keeps the WASAPI render client continuously
fed, so it never underruns and stops when the audio goes quiet (e.g. a paused
movie).

### Clock drift

The capture clock (default device) and each output device's clock are never
perfectly equal, so over a long session an output's buffered backlog slowly
drifts. Both directions are bounded:

- **Output running slow** → backlog grows → `DriftSampleProvider` discards a
  small, frame-aligned slice (an occasional, near-inaudible skip) once it passes
  the ceiling.
- **Output running fast** → backlog empties → the silence pump inserts silence,
  effectively holding the device back.

The buffer therefore hovers in a bounded band instead of desyncing over hours.

## Staying alive over long sessions

Movies and series run for hours and cross events that would otherwise kill audio.
A **4-second watchdog** in `AudioEngine` heals these without user action:

- **Sleep / resume** — audio endpoints are dead after resume. Loopback capture
  raises `RecordingStopped` (flagged) and `PowerModeChanged.Resume` also flags a
  rebuild; the watchdog rebuilds capture and all channels on its next tick.
- **Silent stall / dropped device** — if an output stops without a system
  notification (some Bluetooth stacks), the watchdog notices the channel is no
  longer `Playing` and recreates it.
- **Device returned** — a channel stuck in `Reconnecting` is retried until the
  device is active again.

Healthy channels are left untouched (checked by state), so recovery never
glitches audio that's already playing.

## Threading model

- **UI thread** — WPF window and view models.
- **WASAPI capture thread** — `AudioEngine.OnDataAvailable` copies each buffer
  into every channel's `BufferedWaveProvider` (thread-safe, `ConcurrentDictionary`).
- **WASAPI render threads** — one per `WasapiOut`, pulling through the chain.
- **Watchdog thread** — a `System.Threading.Timer`; uses `Monitor.TryEnter` so it
  never piles up behind a busy lock.
- **Device / power callbacks** — arrive on system threads; `DeviceMonitor` and the
  engine raise plain events, and `MainViewModel` marshals them to the UI via the
  dispatcher.

All channel/capture mutation happens under a single `_sync` lock in the engine.

## Persistence & packaging

- Settings: `%AppData%\FreakyNikki\settings.json` (per-device enabled/volume/delay,
  atomic writes, debounced).
- Log: `%AppData%\FreakyNikki\log.txt` (rolling, ~1 MB cap).
- Distribution: a self-contained Velopack installer plus a portable zip, built by
  `vpk` in CI and published to GitHub Releases; the app auto-updates from there.

## Known limits

- Windows only (WASAPI / NAudio).
- Two A2DP streams on a single weak Bluetooth adapter can stutter — outside the
  app's control.
- Sync is tuned by ear; there is no automatic latency measurement because Windows
  doesn't expose the Bluetooth codec latency reliably.
