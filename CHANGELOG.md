# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-07-05

### Added
- Initial implementation: mirror system audio to multiple output devices via
  WASAPI loopback capture.
- Per-device volume and delay controls; delay is editable to the millisecond and
  pre-fills a sensible default (~150 ms) for Bluetooth devices.
- Device hot-plug handling and automatic reconnection.
- Minimal, modern UI that follows the Windows light/dark theme and accent color.
- Tray icon with show / start-stop / quit.
- Persistent per-device settings in `%AppData%\FreakyNikki\settings.json`.
- Windows installer with automatic updates from GitHub Releases (via Velopack).
- Long-session resilience: a watchdog recovers audio after sleep/resume, a silent
  Bluetooth stall, or a device dropping and returning.

[Unreleased]: https://github.com/keremkocatus/freaky-nikki/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/keremkocatus/freaky-nikki/releases/tag/v0.1.0
