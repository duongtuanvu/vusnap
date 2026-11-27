# Third-party source reuse

VuSnap remains MIT. Before each feature, evaluate upstream license, exact commit, security, runtime compatibility, maintainability and tests. Never transplant GPL code into this MIT distribution.

## Advanced Capture

- Reused/adapted: [Snapboard WindowEnumerator.cs](https://github.com/Flowdesktech/Snapboard/blob/379651086cd7ce6eb50733925ffe4c74f006fdab/Snapboard/Helpers/WindowEnumerator.cs), MIT, Copyright (c) 2026 FlowDesk. DWM extended-frame bounds and visible window enumeration. The root LICENSE includes the copyright notice and ships in both installer and portable packages.
- Reviewed: [iPrtSc](https://github.com/1tsok/iPrtSc/tree/07c728f0c2388d7bbca4f196f164d9b20e3b5ea4), MIT, opaque Bgr32 capture approach; implemented via existing VuSnap GDI path using Format32bppRgb and LockBits.
- Reviewed: [SnipCanvas](https://github.com/gruzker/SnipCanvas/tree/65db8910e87dfd28dc0b56e3bd639173f09f16dc), MIT, selection and Windows Graphics Capture approach. WGC integration adapted in v0.3.1; GPU/device/OS tests remain open.
- ShareX and Greenshot are GPL-3.0; no code copied.

This increment captures the visible desktop pixels, not hidden window content. Occluding windows may appear. No PrintWindow fallback, activation of arbitrary targets, or bypass of protected content. Window picker initially operates on the monitor under the cursor; mixed-DPI and full-desktop editor acceptance remain open.

## WGC engine

- Adapted [SnipCanvas GraphicsWindowCapture.cs](https://github.com/gruzker/SnipCanvas/blob/65db8910e87dfd28dc0b56e3bd639173f09f16dc/GraphicsWindowCapture.cs), MIT, Copyright (c) 2026 SnipCanvas contributors. Root LICENSE includes attribution. Changes: explicit window identity, bounded frames, linked timeout, cancellation, and error codes. WGC active/list modes never fall back to desktop capture. Separate WindowHover mode is explicitly a visible desktop crop.

## OCR

- Uses Windows.Media.Ocr (Windows SDK) and optional user-installed [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) via documented stdin/stdout CLI. No Tesseract binaries or language models redistributed. Models are user-installed and recognition is local. [Microsoft OCR language pack documentation](https://learn.microsoft.com/windows/powertoys/text-extractor).

## Color and barcode tools (v0.4.3)

- Barcode decoding: [ZXing.Net 0.16.11](https://www.nuget.org/packages/ZXing.Net/0.16.11), Apache-2.0, pinned NuGet package. Reviewed upstream `RGBLuminanceSource`, `BarcodeReaderGeneric`, `DecodingOptions` and license at [e701eec](https://github.com/micjahn/ZXing.Net/tree/e701eec9ab19807a0f5b671ce632db659f8a3a13) (source review revision, not asserted to be the package build revision). Full Apache-2.0 text appended to bundled LICENSE; VuSnap code remains MIT.
- Color picker reuses existing GDI/WGC snapshot architecture and WPF pixel operations; no native color-picker dependency. All analysis is local and explicitly triggered; no automatic URL navigation, clipboard write, image history or persistence.
