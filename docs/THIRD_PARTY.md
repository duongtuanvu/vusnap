# Third-party source reuse

VuSnap remains MIT. Before each feature, evaluate upstream license, exact commit, security, runtime compatibility, maintainability and tests. Never transplant GPL code into this MIT distribution.

## Advanced Capture

- Reused/adapted: [Snapboard WindowEnumerator.cs](https://github.com/Flowdesktech/Snapboard/blob/379651086cd7ce6eb50733925ffe4c74f006fdab/Snapboard/Helpers/WindowEnumerator.cs), MIT, Copyright (c) 2026 FlowDesk. DWM extended-frame bounds and visible window enumeration. The root LICENSE includes the copyright notice and ships in both installer and portable packages.
- Reviewed: [iPrtSc](https://github.com/1tsok/iPrtSc/tree/07c728f0c2388d7bbca4f196f164d9b20e3b5ea4), MIT, opaque Bgr32 capture approach; implemented via existing VuSnap GDI path using Format32bppRgb and LockBits.
- Reviewed: [SnipCanvas](https://github.com/gruzker/SnipCanvas/tree/65db8910e87dfd28dc0b56e3bd639173f09f16dc), MIT, selection and Windows Graphics Capture approach. WGC integration deferred pending GPU/device/OS tests.
- ShareX and Greenshot are GPL-3.0; no code copied.

This increment captures the visible desktop pixels, not hidden window content. Occluding windows may appear. No PrintWindow fallback, activation of arbitrary targets, or bypass of protected content. Window picker initially operates on the monitor under the cursor; mixed-DPI and full-desktop editor acceptance remain open.
