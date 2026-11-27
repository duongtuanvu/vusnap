# VuSnap

A lightweight Windows screenshot and annotation tool built with C#, WPF and .NET 10. VuSnap is independently maintained by vudt and based on [NinoMark v0.5.0](https://github.com/TokisakiNinoVn/NinoMark/commit/bab3f157d21a0ae52b2a966570c492b5f4c80da7).

[Tiếng Việt](README.vi.md) · [Releases](https://github.com/duongtuanvu/vusnap/releases) · [Issues](https://github.com/duongtuanvu/vusnap/issues)

[![CI](https://github.com/duongtuanvu/vusnap/actions/workflows/ci.yml/badge.svg)](https://github.com/duongtuanvu/vusnap/actions/workflows/ci.yml)

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

A tag `v0.1.0` triggers the release workflow for the installer and portable package. No release is implied by this initial source import.

## License and attribution

MIT. See [LICENSE](LICENSE) for the original copyright of Tokisaki Nino and the VuSnap copyright notice. Original assets are retained temporarily; upstream history is documented in [docs/UPSTREAM_CHANGELOG.md](docs/UPSTREAM_CHANGELOG.md).
