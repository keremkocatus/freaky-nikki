# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial implementation: mirror system audio to multiple output devices via
  WASAPI loopback capture.
- Per-device volume and delay (0–500 ms) controls.
- Device hot-plug handling and automatic reconnection.
- Minimal, modern UI that follows the Windows light/dark theme and accent color.
- Tray icon with show / start-stop / quit.
- Persistent per-device settings in `%AppData%\FreakyNikki\settings.json`.

[Unreleased]: https://github.com/keremkocatus/freaky-nikki/commits/main
