# Changelog

## [0.3.1] - Unreleased

- WGC isolated HWND capture with D3D11, bounded frame allocation, timeout and cancellation.
- Explicit window list with live DWM preview; retain GDI region, monitor and visible-window hover modes.
- Reject desktop fallback for isolated window capture. Add WGC geometry and routing regression coverage.

## [0.3.0] - Unreleased

- Add region, monitor, desktop, active window, window picker and repeat capture modes.
- Add cancellable delayed capture and localized tray/main capture controls.
- Capture physical monitor intersections with opaque pixels and bounded dimensions.
- Adapt MIT Snapboard DWM window bounds; preserve attribution.
- Add geometry, layout, settings and cancellation regression tests.

## [0.2.1] - Unreleased

- Restore annotation history from metadata instead of retaining WPF visuals; coalesce slider and color gestures.
- Recover interrupted mouse capture and guard stale text-editor callbacks.
- Harden shortcut parsing, settings updates and hotkey rollback.
- Extract image export and test concurrent no-overwrite publication and image encoding.

## [0.2.0] - Unreleased

- Bounded annotation state history with undo/redo for layer edits.
- Validate settings and shortcut conflicts; atomic settings and image writes.
- Unique quick-save paths, asynchronous encoding and clipboard retries.
- Local rolling diagnostic logs without image content or exception messages.
- Remove forced UI-thread garbage collection; add core regression runner in Windows CI.

## [0.1.0] - 2026-11-27

- Initialize VuSnap from NinoMark v0.5.0; independent application identity and MIT attribution.

## [0.4.0] - Unreleased

- Local OCR dialog, installed Windows OCR language packs and optional installed Tesseract eng/vie/bilingual engine, cancellation and image/output limits.

## [0.4.3] - Unreleased

- Screen snapshot and selected-image color inspector with pixel loupe, keyboard sampling, HEX/RGB/HSV and explicit clipboard actions.
- Local QR and common barcode decoding via pinned ZXing.Net, bounded inputs and output, background execution and synthetic regression coverage.
