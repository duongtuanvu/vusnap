# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-11-26

### Added
- "Check for updates" button in the About window. It opens the latest GitHub release in your browser; the app itself still makes no network connections.
- Website at https://nino-mark.vercel.app (English and Vietnamese), linked from the About window.
- Screenshot in the README.

### Fixed
- The installer icon looked blurry because its icon file had a single 256px frame. It now uses a multi-size icon (16 to 256px).

## [0.4.1] - 2026-11-26

### Added
- Windows installer (`NinoMark-<version>-setup.exe`, built with Inno Setup) alongside the portable zip.

### Changed
- Donate link now points to https://tokisaki-nino.pages.dev/donate.
- Release packages no longer include the `.pdb` debug symbols, and the framework-dependent zip was dropped.

## [0.4.0] - 2026-11-25

### Changed
- Renamed the app from SnapMark to NinoMark. Existing settings and the start-with-Windows entry migrate automatically on first launch.
- The `SNAPMARK_DATA_DIR` environment variable is now `NINOMARK_DATA_DIR`.
- Lower memory use: the working set is trimmed when the app sits in the tray and after a capture, dropping idle RAM from ~160 MB to under 10 MB.

## [0.3.0] - 2026-11-21

First public release.

### Added
- Global hotkey capture (default `Alt+Shift+S`) with an in-place annotation overlay.
- Tools: select, text, pen, line, arrow, rectangle, ellipse, highlight, blur, invert, number, ruler, magnifier.
- Line types: plain, ticked ends, dotted ends, arrow ends.
- Configurable font weight (light, normal, semi-bold, bold) and font family for text.
- Edit text by clicking it with the Text tool; edit numbers by clicking them with the Number tool.
- Hold `Ctrl` with any tool to select and move objects directly.
- Layers panel (rename, show/hide, multi-select, reorder) and object quick bar (duplicate, rotate, flip).
- Aspect-ratio lock, alignment grid, magnifier loupe while selecting.
- Settings window: shortcuts, toolbar position and layout, tool visibility and order, default styles.
- Settings import / export to a JSON file.
- System tray, start with Windows, English and Vietnamese UI, light / dark / system theme.

### Fixed
- Icons and search field icon were unreadable on the dark theme at first launch.

[Unreleased]: https://github.com/TokisakiNinoVn/NinoMark/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/TokisakiNinoVn/NinoMark/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/TokisakiNinoVn/NinoMark/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/TokisakiNinoVn/NinoMark/releases/tag/v0.4.0
[0.3.0]: https://github.com/TokisakiNinoVn/NinoMark/releases/tag/v0.3.0
