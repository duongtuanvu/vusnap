# VuSnap

![VuSnap logo](docs/images/logo.png)

A lightweight Windows screenshot and annotation tool built with C#, WPF and .NET 10. VuSnap is independently maintained by vudt and based on [NinoMark v0.5.0](https://github.com/TokisakiNinoVn/NinoMark/commit/bab3f157d21a0ae52b2a966570c492b5f4c80da7).

[Tiếng Việt](README.vi.md) · [Releases](https://github.com/duongtuanvu/vusnap/releases) · [Issues](https://github.com/duongtuanvu/vusnap/issues)

[![CI](https://github.com/duongtuanvu/vusnap/actions/workflows/ci.yml/badge.svg)](https://github.com/duongtuanvu/vusnap/actions/workflows/ci.yml)

## Windows Graphics Capture (0.3.1)

Active Window and Select Window use WGC to capture the selected window directly; select a window from the list with live DWM preview. Hover Window uses the visible desktop crop. Region/Monitor/Desktop/Repeat remain GDI. WGC requires Windows 10 version 2004 or later and a supported graphics device. No silent fallback to the desktop occurs. Owner reports initial runtime success; detailed GPU, occlusion and DPI acceptance remains open.

## Advanced capture

Choose the capture mode and delay in the main window, or use the tray menu. Repeat reuses the last successfully exported region in the current session when monitor layout is unchanged. Window modes capture visible desktop pixels, including occlusions; they do not capture hidden window content. Press the capture hotkey again to cancel the countdown. See [third-party reuse](docs/THIRD_PARTY.md).

## Features

- Global capture shortcut: Alt+Shift+S; region selection, resizing, loupe, grid and aspect ratio.
- In-place annotation: text, pen, line, arrow, rectangle, ellipse, highlight, pixelation, invert, number, ruler and magnifier.
- Layer selection, reordering, visibility, duplication, rotation and flipping; basic undo/redo.
- PNG/JPEG output, quick save and clipboard copy.
- Custom shortcuts and toolbar, English/Vietnamese, light/dark/system theme, tray and optional startup.
- Local settings: `%AppData%\VuSnap\settings.json`; override with `VUSNAP_DATA_DIR`.

## Build (Windows 10/11, .NET 10 SDK)

```powershell
dotnet build VuSnap.sln -c Release
dotnet run --project src/VuSnap/VuSnap.csproj
```

Publish:

```powershell
dotnet publish src/VuSnap/VuSnap.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

A version tag such as `v0.2.1` triggers the release workflow for the installer and portable package. No release is implied by this initial source import.

## License and attribution

MIT. See [LICENSE](LICENSE) for the original copyright of Tokisaki Nino and the VuSnap copyright notice. VuSnap uses independent logo and icon assets; upstream history is documented in [docs/UPSTREAM_CHANGELOG.md](docs/UPSTREAM_CHANGELOG.md).

## Offline OCR (v0.4.0)

Select a screenshot region and press **OCR**. Choose an available engine/language, then Recognize. Windows OCR uses installed Windows OCR language packs. For reliable Vietnamese or mixed Vietnamese/English, install Tesseract OCR into the standard Program Files/Tesseract-OCR directory with `vie.traineddata` and `eng.traineddata` in its `tessdata` folder. VuSnap only exposes installed languages; it never downloads models or executes a program found via PATH. Recognition uses memory/stdin, not temporary screenshot files. Copy selected text or all results. No automatic clipboard write, network upload or history. Maximum 16 MP and 30-second timeout; Windows OCR may have a lower dimension limit. OCR results require review.

## Color Picker and QR / Barcode (v0.4.3)

- Choose **Screen color picker** in the capture modes (or tray menu) and capture the monitor under the cursor. The inspector displays a frozen image; hover to sample, click to lock, arrow keys move one pixel, and copy HEX/RGB/HSV individually. Resume to unlock. This is not continuous live desktop capture.
- In the screenshot editor, select the area and press **RGB** to inspect exported pixels or **QR** to decode a single QR/barcode from the rendered selection. ZXing.Net runs locally. Choose a tight region for tiny codes; max 12 MP and 8192 px per dimension. Closing the result cancels publication; an in-flight synchronous decode finishes in the worker before another starts. QR data is plain text, never automatically opened or copied. Supported formats include QR, Data Matrix, Aztec, PDF417, Code128/39, EAN/UPC, ITF and Codabar.

### OCR layout and language

The OCR result is reconstructed from word bounding boxes, with monospaced output and horizontal scrolling for columns. Tesseract uses TSV positions and automatic page segmentation. If installed, Vietnamese + English is preferred; Windows English-only recognition cannot reliably recover Vietnamese accents. Install Tesseract eng/vie language data to use bilingual OCR. Layout is approximate plain text, not a pixel-identical document or editable Excel table.
