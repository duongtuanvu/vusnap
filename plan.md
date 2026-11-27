# VuSnap — Development Plan

> Single source of truth for development scope, implementation progress and acceptance.
> Updated: 2026-11-27 (Asia/Bangkok).
> Current merged application version: **0.2.1**. CI portable and installer artifacts are available; no public release/tag has been verified.

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

- [ ] Introduce capture request/result model, capture modes and consistent error handling.
- [ ] Capture active window and selectable window with hover highlight; exclude own UI.
- [ ] Full-screen capture (current monitor) and all-monitor capture with negative coordinates and mixed DPI.
- [ ] Delayed capture (3/5/10 seconds), cancellation and non-blocking countdown.
- [ ] Repeat last capture region with monitor topology validation.
- [ ] Improve window detection and fallback for unavailable/minimized/protected content; no bypass of OS restrictions.
- [ ] Evaluate Windows Graphics Capture vs existing GDI backend; retain reliable fallback.
- [ ] Add capture mode preferences, shortcuts, localized UI and tests.
- [ ] Verify DPI, 4K, multi-monitor, cancellation and resource disposal; CI pass and merge.

### v0.3.0 candidate — pending CI and merge

- Implemented six capture modes, countdown cancellation, tray selection and settings.
- Reused MIT Snapboard DWM bounds; see [THIRD_PARTY.md](docs/THIRD_PARTY.md).
- Window capture captures visible desktop pixels only; covered windows may be included. Repeat stores coordinates/layout in memory for this session. Mixed-DPI and physical multi-monitor acceptance remain open.
- Added four regression groups for geometry, layout, window hit order, settings and cancellation. Checkboxes remain open until verified merge.

## v0.4.0 — OCR, Pin & History

- [ ] Offline OCR (evaluate Windows OCR and local alternatives; EN/VI support with explicit language availability).
- [ ] Select/copy recognized text, confidence/empty-result handling and optional region OCR.
- [ ] Pin screenshot as movable, resizable, always-on-top window with opacity and close controls.
- [ ] Local capture history: thumbnails, date/search/filter, open/copy/delete.
- [ ] Configurable history retention/size limits; opt-out and clear-history controls.
- [ ] Standalone color picker (HEX/RGB/HSV), QR/barcode decoding where appropriate.
- [ ] No cloud upload or telemetry by default; verify privacy and cleanup.
- [ ] Tests, CI pass and merge.

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

## Next task

Next feature increment: v0.3.0 Advanced Capture, beginning with capture request/result model and current-monitor/full-window modes. Keep unresolved v0.2.x manual Windows acceptance and benchmarks visible as a parallel gate; do not mark them complete without evidence or release a production version with blocking defects.
