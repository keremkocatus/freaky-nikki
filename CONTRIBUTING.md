# Contributing to Freaky Nikki

Thanks for your interest! This is a small, focused open-source project and
contributions are welcome.

## Getting started

1. Install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (Windows).
2. Clone the repo and build:
   ```bash
   dotnet build
   dotnet run --project src/FreakyNikki
   ```

## Project layout

- `src/FreakyNikki/Audio/` — the audio engine (loopback capture, per-device output
  pipeline, device monitoring). This is the heart of the app.
- `src/FreakyNikki/Theme/` — system light/dark + accent theming.
- `src/FreakyNikki/ViewModels/` + `MainWindow.xaml` — the UI (MVVM).
- `src/FreakyNikki/Settings/` — JSON settings persistence.
- `design/plan.md` — architecture and roadmap.

## Guidelines

- Keep it **minimal**. The whole point is a tiny, driver-free, single-exe tool.
  New dependencies should earn their place.
- Match the existing style (`.editorconfig` is enforced — file-scoped namespaces,
  4-space indent, `nullable` enabled).
- Audio changes are hard to unit-test; please describe how you tested them
  manually (which devices, BT/wired, what you heard).
- Run `dotnet build -c Release` before opening a PR — it should be warning-free.

## Reporting bugs

Open an issue with:
- Windows version
- The devices involved (Bluetooth / wired, model if relevant)
- What you expected vs. what happened
- The log at `%AppData%\FreakyNikki\log.txt`, if useful

## Commit / PR

- Branch off `main`, keep PRs focused.
- Update `CHANGELOG.md` under `[Unreleased]` when you change user-facing behavior.
