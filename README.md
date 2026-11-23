**English** | [Português (Brasil)](README.pt-BR.md)

<p align="center">
  <a href="https://learn.microsoft.com/dotnet/csharp/"><img alt="C#" src="https://img.shields.io/badge/C%23-14-68217A?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTEyIDEgMi41IDYuNXYxMUwxMiAyM2w5LjUtNS41di0xMXoiLz48dGV4dCB4PSIxMiIgeT0iMTUuNSIgZm9udC1mYW1pbHk9IkFyaWFsLHNhbnMtc2VyaWYiIGZvbnQtc2l6ZT0iOC41IiBmb250LXdlaWdodD0iNzAwIiB0ZXh0LWFuY2hvcj0ibWlkZGxlIiBmaWxsPSIjNjgyMTdBIj5DIzwvdGV4dD48L3N2Zz4=&logoColor=white"></a>
  <a href="https://dotnet.microsoft.com/"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"></a>
  <a href="#installation"><img alt="Windows, Linux and macOS" src="https://img.shields.io/badge/Windows%20%C2%B7%20Linux%20%C2%B7%20macOS-platforms-444?style=for-the-badge"></a>
  <a href="https://avaloniaui.net/"><img alt="Avalonia" src="https://img.shields.io/badge/Avalonia-Fluent-0C54C2?style=for-the-badge"></a>
  <a href="https://ffmpeg.org/"><img alt="FFmpeg 9" src="https://img.shields.io/badge/FFmpeg-9-007808?style=for-the-badge&logo=ffmpeg&logoColor=white"></a>
  <a href="https://github.com/naudio/NAudio"><img alt="NAudio 2.2" src="https://img.shields.io/badge/NAudio-2.2-E65100?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTMgOXY2aDRsNSA1VjRMNyA5em0xMy41IDNBNC41IDQuNSAwIDAgMCAxNCA4djhhNC41IDQuNSAwIDAgMCAyLjUtNHpNMTQgMy4ydjIuMWE3IDcgMCAwIDEgMCAxMy40djIuMWE5IDkgMCAwIDAgMC0xNy42eiIvPjwvc3ZnPg==&logoColor=white"></a>
  <a href="https://github.com/shimat/opencvsharp"><img alt="OpenCvSharp 4.13" src="https://img.shields.io/badge/OpenCvSharp-4.13-5C3EE8?style=for-the-badge&logo=opencv&logoColor=white"></a>
  <a href="https://onnxruntime.ai/"><img alt="ONNX Runtime 1.24" src="https://img.shields.io/badge/ONNX%20Runtime-1.24-005CED?style=for-the-badge&logo=onnx&logoColor=white"></a>
  <a href="https://github.com/microsoft/onnxruntime-genai"><img alt="ONNX Runtime GenAI 0.14" src="https://img.shields.io/badge/ONNX%20Runtime%20GenAI-0.14-005CED?style=for-the-badge&logo=onnx&logoColor=white"></a>
  <a href="https://github.com/ZHKKKe/MODNet"><img alt="MODNet" src="https://img.shields.io/badge/MODNet-matting-0A7E8C?style=for-the-badge&logo=data:image/svg%2Bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZmlsbD0iI2ZmZiIgZD0iTTEyIDEyYTUgNSAwIDEgMCAwLTEwIDUgNSAwIDAgMCAwIDEwem0wIDJjLTQuNCAwLTkgMi4yLTkgNXYzaDE4di0zYzAtMi44LTQuNi01LTktNXoiLz48L3N2Zz4=&logoColor=white"></a>
  <a href="CODE_OF_CONDUCT.md"><img alt="Contributor Covenant 3.0" src="https://img.shields.io/badge/Contributor%20Covenant-3.0-5E0D73?style=for-the-badge&logo=contributorcovenant&logoColor=white"></a>
</p>

# Meu Editor de Vídeo

A non-linear video editor for **Windows 11 (64-bit)**, **Linux (64-bit)** and **macOS**, built from one code base, with
its interface and manual in **six languages** and 100% local processing, artificial intelligence included. No video,
audio, image or text ever leaves the computer.

Version **1.4.0**.

## Installation

| System | Package | Requirements |
|---|---|---|
| Windows | `Setup.exe` (recommended) or `.msi` | Windows 11 64-bit (x64) |
| Linux | `.deb`: `sudo apt install ./meu-editor-de-video_<version>_amd64.deb` | Debian 12+ or Ubuntu 22.04+, x64 |
| macOS | `.dmg`: drag the app to **Applications** | macOS 12+, Apple silicon (arm64) or Intel (x64) |

Step by step, silent installation, uninstalling and the development environment on each system:
[`INSTALL.md`](INSTALL.md). Limits that depend on the system (Wayland, Gatekeeper, AI on Intel Macs) are listed in
[Platform notes](#platform-notes).

## Documentation

- **Installation** (end users and development environment): [`INSTALL.md`](INSTALL.md)
- User manual: [`documentation/MANUAL_DO_USUARIO.en.md`](documentation/MANUAL_DO_USUARIO.en.md) (also in
  **Help › User manual**, F1; available in [Portuguese (Brazil)](documentation/MANUAL_DO_USUARIO.md),
  [Spanish](documentation/MANUAL_DO_USUARIO.es.md), [French](documentation/MANUAL_DO_USUARIO.fr.md),
  [German](documentation/MANUAL_DO_USUARIO.de.md) and [Portuguese (Portugal)](documentation/MANUAL_DO_USUARIO.pt-PT.md))
- The documents below are written in Portuguese:
  - Technical documentation: [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
  - Installer and code signing: [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md)
  - Microsoft Store publishing: [`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md)
  - AI models: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
  - Acceptance testing (CA001–CA024): [`documentation/HOMOLOGACAO.md`](documentation/HOMOLOGACAO.md)
  - Dependencies and licenses: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md) and
    [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt)
  - Privacy policy: [`POLITICA_DE_PRIVACIDADE.md`](POLITICA_DE_PRIVACIDADE.md)
  - Specification: [`ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip`](ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip)

## Features

### Interface and languages

- Interface and manual in **English, French, German, Portuguese (Brazil), Portuguese (Portugal) and Spanish**. On the
  first run, the **welcome** window asks for your name, language, theme and whether the program should check for
  updates; changing the language restarts the program in it.
- Opens straight into the editor with an empty project; the **File** menu lists the five most recent projects.
- Custom title bar, **Light**, **Dark** and **Same as the system** themes, tabbed library and a full-height
  **Properties** panel; on macOS the menus become the native menu bar. In the bottom-right corner, one-click shortcuts to the **User manual** and the
  **Virtual assistant**.
- **View**: layout modes (Default, Timeline, Video), playback quality, monitor zoom (Fit to 400%), loop playback and a
  pixel ruler.
- Customizable keyboard shortcuts, with conflict warnings. On macOS they use **Cmd** and **Option** instead of Ctrl and
  Alt, in every language.
- Dialogs fit the usable area of small screens: they shrink, their content scrolls, and their buttons stay in view.
- Accessibility: screen-reader names (UI Automation, NSAccessibility, AT-SPI) on controls, lists and grids.

### Media and projects

- Imports video (MP4, MOV, MKV, WebM, AVI, MXF and more), audio (MP3, WAV, AAC, FLAC, Opus and more), images (JPG, PNG,
  WebP, TIFF, **SVG** and more), animated GIFs and numbered image sequences.
- 540p **proxies** to preview heavy videos; export always uses the original.
- `.med` projects with linked or **packaged** media, atomic saves verified with SHA-256, **autosave** and crash
  recovery, backups and relinking of missing media.
- **Local packs** (`.medpack`) of stickers, titles and project templates, with the **Basic** pack of 40 stickers
  included.

### Timeline

- Multiple video, audio, text, image and subtitle tracks; locked, hidden and muted tracks.
- Split and **join cuts**, trim, copy, paste at the playhead, duplicate, delete, **delete and close the gap** and
  **delete and join** (across all tracks).
- **Snapping**, **ripple editing**, grouping and linking clips, per-clip color, markers, zoom and undo/redo with
  history.
- Constant and curved **speed**, **reverse playback**, freeze frame and pitch preservation.
- **Adjustment layers**, **compound clips**, **split screen** with templates, **multicam** (Alt+1 to Alt+4) and
  disabled clips.

### Image, color and compositing

- Position, scale, rotation, opacity, **2.5D perspective**, blend modes and keyframes on every property.
- **Crop** and **Apply crop to video** (the project and the export take the cropped size).
- **Auto reframe** to 9:16, 1:1, 4:5 or 16:9, following the face, the body or the center of the frame.
- Effects with thumbnails: blur, sharpen, vignette, grain, **chroma key**, **edge refine**, **360° reframe**, **lens
  correction**, **video denoise**, **audio visualizer** and more; before/after comparison on the monitor.
- Color: **presets**, color adjustments, white balance, **curves**, **color wheels**, **3D LUTs (.cube)**, **HSL** per
  color range, **HDR** and **color match** between clips.
- **Masks** (rectangle, ellipse, freehand, with shape keyframes), effect-only masks, **motion tracking** and
  **stabilization**.
- Graphic elements (shapes and SVG), **text** with templates (title, lower third, rolling credits), custom fonts,
  in and out animations and saved titles.
- **Transitions** between clips.

### Audio

- **Mixer** with volume, pan, mute, solo and level meters.
- Volume keyframes, fades, **loudness normalization** (EBU R128), **silence removal**, **beat markers** and **cut
  videos to the beat**, **sync by audio** and **auto ducking** of music under speech.
- Effects: equalizer, compressor, noise reduction, echo, reverb, telephone/radio, distortion and **voice changer**.

### Artificial intelligence (local)

- **Background removal** (MODNet, included) with a transparent, color, blurred or media background; masks computed in
  the background and **person tracking**.
- **Generate subtitles** from speech (Whisper), **voice isolation** and **noise reduction** (GTCRN), **remove
  non-speech parts** (Silero VAD), **scene detection** (TransNetV2), 2× **image enhancement** (Swin2SR) and face
  **retouching** with beauty, makeup and face blur (MediaPipe Face Mesh V2).
- **Virtual assistant** (Help › Virtual assistant, Shift+F1): answers questions about the program and video editing
  from the manual, in the interface language. With the optional **Phi-4-mini** model installed, it writes the answer
  in natural language and cites the manual section; without it, it shows the sections found.
- Optional models are downloaded once, by the program itself, from their official sources (HTTPS and SHA-256). They
  run on the graphics card (DirectML on Windows, Core ML on Apple silicon Macs) or on the CPU (Linux and Intel Macs).
  **Intel Macs have no ONNX Runtime**: there, features that depend on it (such as background removal and the virtual
  assistant) are turned off with a notice, and only Whisper subtitles work.

### Recording and export

- **Record screen, webcam and microphone** at the same time, choosing the monitor, with the webcam in a corner, a
  countdown and **F10** to stop. On Linux the screen is recorded under Xorg (X11); on Wayland, screen recording is
  unavailable and the webcam and microphone still work.
- **Text to speech** with the voices of the system (SAPI on Windows, `say` on macOS, `espeak-ng` on Linux).
- **Export** with templates for YouTube, Shorts, Instagram, TikTok, WhatsApp, Facebook and more, plus your own
  templates; MP4, MOV, MKV, WebM, AVI, MPEG-2 and GIF; H.264, H.265, AV1, VP9, ProRes, FFV1 and more; hardware encoding
  (NVENC, AMF and QSV on Windows, NVENC and QSV on Linux, VideoToolbox on macOS) or software; alpha channel, range, splitting into parts and subtitles (SRT/VTT or burned in).
- Background **render queue** that resumes interrupted exports.
- **Export the current frame** as PNG or JPEG.
- Subtitles: SRT/VTT import and export and a subtitle editor.

### Settings and privacy

- Hardware detection with recommendations, preview quality, automatic proxies, hardware acceleration, AI device,
  cache limit and cleanup.
- **No telemetry.** The network is used only for **Check for updates** (it sends only the interface language and the operating system) and to
  download AI models when the user clicks **Install**. Automated tests ensure no other part of the program accesses the
  network, and third-party files (videos, SVGs, downloaded projects) cannot open connections.

## Technologies

| Area | Technology |
|---|---|
| Platform | C# and .NET 10 (`net10.0`), published *self-contained* for `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64` from one code base |
| User interface | Avalonia 12 with the Fluent theme, MVVM with CommunityToolkit.Mvvm 8.4 |
| Composition and services | Microsoft.Extensions.Hosting 10 (dependency injection), Serilog (local file logs) |
| Video and audio | FFmpeg 9 (shared build) via FFmpeg.AutoGen 9 (decoding, encoding and muxing); the `ffmpeg` tool records the screen (gdigrab and DirectShow on Windows, x11grab on Linux, avfoundation on macOS) |
| Audio playback | SDL3 (Linux and macOS) and NAudio 2.2 (WASAPI, Windows), master clock of the preview |
| Image and vision | OpenCvSharp 4.13 (tracking and stabilization), SkiaSharp 4 and HarfBuzz (text and shapes), Svg.Skia 5.2 (SVG), custom CPU compositor (2.5D) |
| AI | ONNX Runtime 1.24 with DirectML (Windows GPU), Core ML (Apple silicon) or CPU (none on Intel Macs): MODNet, MediaPipe Face Mesh V2, Silero VAD, GTCRN, TransNetV2, Swin2SR; Whisper.net 1.9 (subtitles) |
| Virtual assistant | Knowledge base of the six manuals with local BM25 search; ONNX Runtime GenAI 0.14 (DirectML on Windows, CPU elsewhere) with Phi-4-mini-instruct int4 |
| Project format | `.med` (ZIP with `manifest.json` and `project.json`, versioned with migrations), time in *flicks* (`long`) |
| Languages | Translation of the visual tree at load time (`Translator`, one `.tsv` file per language) |
| System integration | Code behind `OperatingSystem.Is…()`; F10 global hotkey with SharpHook; preferences in the Windows registry or, on Linux and macOS, in a JSON file |
| Tests | xUnit, NetArchTest (layering and privacy), Avalonia.Headless (UI), coverlet; `offline-check.ps1` runtime check (Windows, Linux and macOS) |
| Installers | Windows: WiX Toolset 6 (MSI and Setup.exe with the Visual C++ Redistributable) and MSIX for the Microsoft Store; Linux: `.deb`; macOS: `.app` and `.dmg` (signing and notarization optional) |
| CI | GitHub Actions on win-x64, linux-x64, osx-arm64 and osx-x64: build, tests, packages and installation tests |

## Structure

```
src/        Desktop (Avalonia), Presentation (view models), Application, Domain, Core, Infrastructure, ModelDownloads and the engines
            (Project, Media, Timeline, Image, Audio, AI, Rendering)
tests/      UnitTests, IntegrationTests, MediaTests
resources/  export templates, transitions, effects, LUTs, fonts, AI model catalog and translations
scripts/    dependencies, third-party notices, installers, string extraction and offline check
installer/  Msi/ (MSI), Bundle/ (Setup.exe) and Store/ (MSIX)
packaging/  macOS (entitlements and bundle files)
documentation/
```

How to build, test and produce the installers: [`INSTALL.md`](INSTALL.md).

## Platform notes

- **Linux and macOS are validated by the CI only** (there is no local virtual machine); report any difference you find.
- **Linux, Wayland**: the system does not let other programs capture the screen, so screen recording is unavailable
  (with a notice) and **F10** only works while the program is in front. To record the screen, choose an **Xorg**
  session at the login screen.
- **macOS, first launch**: unless the package was signed and notarized, Gatekeeper blocks it. Allow it in
  **System Settings › Privacy & Security › Open Anyway**. Screen recording, camera, microphone and the global **F10**
  ask for their own permissions the first time they are used.
- **macOS, Intel**: no ONNX Runtime (see [Artificial intelligence](#artificial-intelligence-local)).
- **Linux, AI** runs on the CPU, and hardware encoding with VA-API is not enabled yet.
- **Where your data lives** (settings, recent projects, autosave, cache, AI models): on Windows
  `%APPDATA%\MeuEditorDeVideo` and `%LOCALAPPDATA%\MeuEditorDeVideo`; on Linux `~/.config/MeuEditorDeVideo` and
  `~/.local/share/MeuEditorDeVideo`; on macOS `~/Library/Application Support/MeuEditorDeVideo`. Uninstalling keeps it.

## Frequently Asked Questions (FAQ)

**Who can use this software?**

Anyone can use it, as long as the required technologies are installed on the computer.

**Who can contribute?**

Any developer can contribute, following the code of conduct described in the
[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) file.

## License

Meu Editor de Vídeo is free software, distributed under the **GNU General Public License, version 3 or later
(GPL-3.0-or-later)**. You may use, study, modify and redistribute the program under the terms of that license. The
full text is in [`LICENSE.md`](LICENSE.md). Third-party components and their licenses are listed in
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

## Author

**Diego Mendes Rodrigues** — [LinkedIn](https://www.linkedin.com/in/diegomendesrodrigues/)
