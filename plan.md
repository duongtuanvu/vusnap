# VuSnap — Development Plan

> Single source of truth for development scope, implementation progress and acceptance.
> Updated: 2026-11-27 (Asia/Bangkok).
> Current merged application version: **0.4.0**. CI verifies packaging without uploading artifacts; preview is manual. No public release/tag has been verified.

## Working agreement

- Work on a dedicated feature/fix branch from the latest `main`.
- Implement security, performance, accessible/responsive UI, explicit error codes and regression coverage together with each change.
- Open a PR, review the diff, and verify CI build and tests for its **latest head SHA**. Resolve failures before merging. Never skip or weaken tests merely to pass CI.
- Merge automatically once required checks pass, the PR is mergeable, and no known blocking defects remain. Do not bypass branch protection, force-push, or merge failed/pending CI.
- After merge, verify `main` and its CI. Update this plan in the repository after each completed increment, recording PR, commit, test evidence, and outstanding acceptance criteria. Prefer checklist updates within the feature PR when the implementation and evidence are available; record verified merge outcomes afterwards.
- Mark `[x]` only for completed, merged scope with appropriate evidence. Implementation and runtime acceptance are tracked separately. Unchecked items are not claims of completion.
- Do not publish releases or make breaking changes to the public contract without appropriate validation. No automatic release is implied by a version in this plan.
- For work performed by the assistant, merge during an active task after checks are verified; there is no unattended background monitoring unless separately configured.

## v0.1.0 — Import & Rebrand

Status: merged; CI passed.

- [x] Import NinoMark v0.5.0 source, preserve MIT attribution and binary assets.
- [x] Rename application, solution, project, namespace, installer, executable and build configuration to VuSnap.
- [x] Isolate settings, registry, single-instance identifiers and installer identity.
- [x] Update README (EN/VI), upstream attribution and GitHub release links.
- [x] Windows CI baseline build.
- [x] Independent VuSnap logo/icon assets (implemented in v0.2.1; visual runtime acceptance remains open).

Evidence: [baseline commit e47dc696](https://github.com/duongtuanvu/vusnap/commit/e47dc6969f01cfbc1976d44e3f2e9c47143ddad2), [CI](https://github.com/duongtuanvu/vusnap/actions/runs/45957118268).

## v0.2.0 — Core Foundation

Status: implementation merged; CI build and core regression runner passed. Manual WPF acceptance remains open.

- [x] Introduce bounded history abstraction and integrate annotation state capture/restore (50 history steps).
- [x] Add atomic file write helper and unique quick-save path generation.
- [x] Integrate settings validation, bounded import, atomic writes and import hotkey rollback.
- [x] Add structured error codes and local size-bounded diagnostics.
- [x] Implement asynchronous image encoding/writing and clipboard retry.
- [x] Remove forced dispatcher-thread GC and restrictive runtime GC overrides.
- [x] Add regression runner covering history, atomic write, unique names, settings round-trip/validation/import and hotkey conflicts.
- [x] Integrate regression runner into Windows CI; fix missing System.IO import.
- [x] Merge PR #1 after both push and PR CI pass.
- [ ] Complete runtime Undo/Redo acceptance for add/delete, drag, rotate/flip, styles, text/number edits, visibility, rename, reorder and duplication, including rapid gestures and cancellation.
- [ ] Verify runtime image output, clipboard contention, save failure, settings import rollback and concurrency on Windows.
- [ ] Review settings validation compatibility with every UI option; test upgrades from v0.1.0.
- [ ] Measure capture latency, UI responsiveness, allocation/working set and export times (1080p/4K).
- [ ] Separate editor state/model from WPF visuals; verify bounded history memory and gesture coalescing.
- [ ] Create independent logo/icon set and replace upstream branding assets; verify installer/icon scaling.

Evidence: [PR #1](https://github.com/duongtuanvu/vusnap/pull/1), [PR CI](https://github.com/duongtuanvu/vusnap/actions/runs/45957950628), [push CI](https://github.com/duongtuanvu/vusnap/actions/runs/45957943144), [merge 24e9f2e](https://github.com/duongtuanvu/vusnap/commit/24e9f2e6d7d99fba47c8bfcffce163bd155c5bea).

## v0.2.x — Stabilization gate

- [ ] Close unresolved v0.2.0 runtime acceptance items; prioritize correctness and data safety before new editor architecture work.
- [ ] Add focused Windows UI/integration regression coverage.
- [x] Verify portable and installer packaging, including MIT LICENSE (CI build and artifact upload passed; installation smoke test remains open).
- [ ] Record benchmark baseline and remaining non-blocking technical debt.
- [ ] CI and acceptance pass; merge, update plan.

### v0.2.1 — merged implementation and CI evidence

- [x] Implement metadata-only history, gesture coalescing and pointer capture recovery (build passed; manual interaction acceptance remains open).
- [x] Implement hotkey parsing/rollback, serialized settings updates and image export regression tests; all 11 core regression groups passed.
- [x] Independent logo and seven-size ICO; CI portable and installer packaging verification.
- [ ] Manual Windows interaction, installer installation/uninstallation, icon scaling and benchmark acceptance.

Evidence: [PR #2](https://github.com/duongtuanvu/vusnap/pull/2), [PR CI](https://github.com/duongtuanvu/vusnap/actions/runs/45960224061), [push CI](https://github.com/duongtuanvu/vusnap/actions/runs/45960217962), [merge 5879cc5](https://github.com/duongtuanvu/vusnap/commit/5879cc5ba814de0710e3dce42c51226acce9cce2), [CI package artifact](https://github.com/duongtuanvu/vusnap/actions/runs/45960224061/artifacts/15180258020) (temporary retention).

## v0.3.0 — Advanced Capture

- [x] Introduce capture request/result model, capture modes and consistent error handling.
- [ ] Capture active window and selectable window with hover highlight; exclude own UI.
- [ ] Full-screen capture (current monitor) and all-monitor capture with negative coordinates and mixed DPI.
- [x] Implement delayed capture (3/5/10 seconds), cancellation and non-blocking countdown.
- [x] Implement repeat last capture region with monitor topology validation.
- [ ] Improve window detection and fallback for unavailable/minimized/protected content; no bypass of OS restrictions.
- [ ] Evaluate Windows Graphics Capture vs existing GDI backend; retain reliable fallback.
- [x] Add capture mode preferences, existing global shortcut integration, localized UI and tests.
- [ ] Verify DPI, 4K, multi-monitor, cancellation and resource disposal; CI pass and merge.

### v0.3.0 — merged implementation

- Implemented six capture modes, countdown cancellation, tray selection and settings.
- Reused MIT Snapboard DWM bounds; see [THIRD_PARTY.md](docs/THIRD_PARTY.md).
- Window capture captures visible desktop pixels only; covered windows may be included. Repeat stores coordinates/layout in memory for this session. Mixed-DPI and physical multi-monitor acceptance remain open.
- Added four regression groups for geometry, layout, window hit order, settings and cancellation. CI build, regression runner and packaging passed; PR #3 merged.

## v0.3.1 — Capture Engine Upgrade

- [x] Adapt MIT WGC implementation with bounded allocation, cancellation, timeout and resource disposal.
- [x] Select explicit HWND by window list; no silent desktop fallback.
- [x] Integrate WGC active/window selection and preserve GDI region/monitor capture.
- [x] Window thumbnails and live preview with bounded resource usage.
- [ ] Verify actual occluded-window capture, GPU compatibility and mixed DPI on Windows.
- [x] Regression tests, CI and merge.

Owner reports WGC works on Windows; detailed acceptance remains open. [PR #5](https://github.com/duongtuanvu/vusnap/pull/5), [CI](https://github.com/duongtuanvu/vusnap/actions/runs/45968603388), merge 8ef66bf6.

## v0.4.0 — Offline OCR

- [x] Offline OCR via installed Windows languages and optional installed Tesseract eng/vie/bilingual.
- [x] Recognize selected region, editable/copyable text, empty-result handling and cancellation (Windows OCR does not expose confidence).
- [x] Local-only recognition, no automatic upload or history; no temporary screenshot files.
- [ ] Verify actual English/Vietnamese OCR quality, installed Tesseract and cancellation on Windows.
- [x] Tests, CI pass and merge. [PR #6](https://github.com/duongtuanvu/vusnap/pull/6), [CI](https://github.com/duongtuanvu/vusnap/actions/runs/45971425578), merge 7aec925.

## v0.4.3 — Color Picker & QR Scanner

- [ ] Sample screenshot pixels and standalone screen color picker; HEX/RGB/HSV with copy.
- [ ] Decode QR and common barcodes locally; display text, never auto-open decoded URLs.
- [ ] Regression tests, CI and merge.

Pin Screenshot (v0.4.1) and Capture History (v0.4.2) are excluded from the current roadmap by owner request. Do not implement them without a new request.

## v0.5.0 — Advanced Editor & Scrolling Capture

- [ ] Import image, crop/resize/rotate and editable project format with versioning.
- [ ] Layer grouping, improved selection, alignment and history integration.
- [ ] Secure opaque redaction that permanently removes covered pixels from flattened exports; warn that pixelation is not secure redaction.
- [ ] Beautifier: padding, backgrounds, shadows and corner radius.
- [ ] PNG/JPEG/WebP export, target-size compression; evaluate PDF export.
- [ ] Scrolling capture with overlap detection, stitch preview, duplicate rejection, cancellation and memory limits.
- [ ] Image comparison, manual stitching and accessibility polish.
- [ ] Test image quality, privacy, memory bounds, undo/redo and export failure; CI pass and merge.

## v0.6.0 — Screen Recorder

- [ ] Evaluate Windows Graphics Capture and supported encoder pipeline.
- [ ] Record region/window/monitor, start/pause/resume/stop, configurable FPS and resolution.
- [ ] Optional microphone/system audio with visible consent controls and device failure handling.
- [ ] MP4 output and optional GIF export; duration/size limits and atomic finalization.
- [ ] Recording indicator, hotkeys, recovery and resource cleanup.
- [ ] Performance, AV sync, disk-full, device-loss and cancellation tests; CI pass and merge.

## v0.7.0 — Workflow Automation

- [ ] Local workflow engine: capture → OCR → redact → resize/beautify → export/copy.
- [ ] Presets and explicit user-triggered execution, progress, cancellation, timeouts and actionable errors.
- [ ] CLI with documented parameters and machine-readable results.
- [ ] Optional upload provider(s), opt-in credentials, secure secret storage and explicit sharing.
- [ ] Optional MCP integration with least-privilege tools and confirmation for external transfers.
- [ ] Optional AI features, local-first where practical; no mandatory cloud dependency.
- [ ] Tests, threat review, CI pass and merge.

## v1.0.0 — Production Release

- [ ] Finalize UX, accessibility, EN/VI localization and onboarding.
- [ ] Signed installer/executable strategy, trusted update verification and rollback.
- [ ] Windows compatibility matrix; capture/record/editor end-to-end tests.
- [ ] Performance and security audit; dependency/license review.
- [ ] Robust diagnostics, migration, backup and recovery.
- [ ] Publish docs, website and verified release artifacts with checksums.
- [ ] Release candidate acceptance, CI pass, merge and approved v1.0.0 release.

## Progress log

| Date | Version | Result | Evidence |
| --- | --- | --- | --- |
| 2026-11-27 | 0.1.0 | Imported and rebranded; baseline CI passed | e47dc696 |
| 2026-11-27 | 0.2.0 | Core implementation and regression CI passed; merged; manual runtime acceptance open | PR #1 / 24e9f2e |
| 2026-11-27 | 0.2.1 | Stabilization and branding implemented; all 11 regression groups and packaging CI passed; merged | PR #2 / 5879cc5 |

| 2026-11-27 | 0.3.0 | Advanced Capture build, regression and packaging CI passed; merged | [PR #3](https://github.com/duongtuanvu/vusnap/pull/3) / 66e71da |

## Next task

Implement v0.4.3 Color Picker & QR Scanner, then evaluate v0.5.0. WGC and OCR merged; detailed runtime acceptance remains open.

### GitHub Actions resource policy

- CI runs on pull requests, main pushes and explicit dispatch; no automatic feature-branch duplicate run and no artifact upload. PR packaging remains a verification gate without storing binaries.
- Preview build is manual, defaults to portable only, with optional installer and one-day artifact retention. Release remains tag-triggered; do not create tags merely to test.
- Existing artifacts are not deleted automatically. Record CI evidence and merge only after required checks pass.
