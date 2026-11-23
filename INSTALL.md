**English** | [Português (Brasil)](INSTALL.pt-BR.md)

# Installation

This guide has two parts:

1. [Installing the application (end users)](#installing-the-application-end-users): [Windows](#windows),
   [Linux](#linux-debian-and-ubuntu) and [macOS](#macos)
2. [Setting up the development environment](#setting-up-the-development-environment) on Windows, Linux and macOS

---

## Installing the application (end users)

### System requirements

| | Minimum | Recommended |
|---|---|---|
| Operating system | Windows 11 64-bit (x64); Debian 12+ or Ubuntu 22.04+ (x64); macOS 12+ (Apple silicon or Intel) | The same |
| Processor | Intel Core i5 or AMD Ryzen 5 (or Apple silicon M1) | Intel Core i7 or AMD Ryzen 7 |
| Memory | 16 GB | 32 GB |
| Graphics card | Direct3D compatible on Windows; any on Linux and macOS | NVIDIA RTX or AMD Radeon with 8 GB |
| Storage | SSD | 1 TB NVMe SSD |

The program takes about 540 MB once installed on Windows. .NET is bundled, so there is nothing else to install.

On Linux, AI features run on the CPU. On Apple silicon Macs they use Core ML. **Intel Macs have no ONNX Runtime**: the AI
features that depend on it (such as background removal and the virtual assistant) are turned off there, with a
notice; Whisper subtitles still work.

### Windows

#### Which installer to download

The installers are published on the repository's **Releases** page on GitHub, whenever a version is available.

| File | When to use it |
|---|---|
| `MeuEditorDeVideo-<version>-Setup.exe` | **Recommended.** Installs the Visual C++ Redistributable x64 if it is missing, then the program for all users in `C:\Program Files\Meu Editor de Vídeo` (asks for administrator permission). |
| `MeuEditorDeVideo-<version>-x64.msi` | For computers that already have Visual C++ 2015-2026 (14.40 or newer) and for company deployment (GPO, Intune). Lets you choose **just for me** (no administrator, in `%LOCALAPPDATA%\Apps\Meu Editor de Vídeo`) or **for all users**. |
| `SHA256SUMS.txt` | SHA-256 checksums to verify the downloaded files. |

To verify a downloaded file, in PowerShell:

```powershell
Get-FileHash .\MeuEditorDeVideo-1.4.0-Setup.exe -Algorithm SHA256
```

The result must match the corresponding line in `SHA256SUMS.txt`.

#### Installing

1. Run `Setup.exe` (or the `.msi`) and follow the screens.
2. The installers are not code-signed, so on the first download Windows SmartScreen may show "Windows protected your
   PC". Click **More info › Run anyway**. Installation and operation are not affected.
3. Open the program from the Start menu or the desktop shortcut. `.med` files now open in the editor.
4. On the first run, the **welcome** window asks for your name, language, theme and whether the program should check
   for updates.

### Linux (Debian and Ubuntu)

Supported: Debian 12 or newer and Ubuntu 22.04 or newer, 64-bit (x64). The packages are `.deb` files, published on the
repository's **Releases** page together with `SHA256SUMS.txt`.

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
sudo apt install ./meu-editor-de-video_<version>_amd64.deb
```

`apt` installs the required libraries along with the package (it needs `./` before the file name). The program goes to
`/opt/meu-editor-de-video`, with the `meu-editor-de-video` command, the entry in the applications menu, the icons and the
`.med` file type. For narration (**Text to speech**), also install `espeak-ng` (the package recommends it). To
install without the recommended packages: `sudo apt install --no-install-recommends ./…deb`.

- **Screen recording needs an Xorg (X11) session.** On Wayland (the default with GNOME on Ubuntu and Debian) the system
  does not let other programs capture the screen: recording is unavailable, with a notice, and **F10** only works with
  the program in front. Choose an **Xorg** session at the login screen to record. Webcam and microphone work on both.
- No GPU for AI (it runs on the CPU), and hardware video encoding is NVENC or QSV; VA-API is not enabled yet.
- Open with the menu or the `meu-editor-de-video` command; `.med` files open in the editor.

### macOS

Supported: macOS 12 or newer, on Apple silicon (`arm64`) and Intel (`x64`); each architecture has its own `.dmg`
(`MeuEditorDeVideo-<version>-arm64.dmg` and `MeuEditorDeVideo-<version>-x64.dmg`).

1. Open the `.dmg` and drag **Meu Editor de Vídeo** to **Applications**.
2. **First launch.** Unless the package was signed and notarized, macOS Gatekeeper blocks it. Open
   **System Settings › Privacy & Security** and click **Open Anyway** (on macOS 14 and earlier: right-click the app ›
   **Open**).
3. The first time you record the screen, use the camera or the microphone, or press **F10** with the program in the
   background, macOS asks for the matching permission in **System Settings › Privacy & Security** (Screen Recording,
   Camera, Microphone and Accessibility). If a recording comes out black or **F10** does not respond, check them.
4. On Mac keyboards, use **Cmd (⌘)** and **Option (⌥)** where this manual and the shortcuts list say Ctrl and Alt; F1,
   F10 and F11 may need the **fn** key. The menus are the native menu bar.
5. **Intel Macs** have no ONNX Runtime (see the requirements above).

### Optional AI models

Background removal (MODNet) comes preinstalled. The other models (Whisper subtitles, voice isolation, scene detection,
face retouching, the Phi-4-mini virtual assistant and more) are installed by the program itself, in
**Tools › AI Models… › Download models…**, with the **Install** button. Each model is downloaded only once from its official source
and verified with SHA-256. Details in the [user manual](documentation/MANUAL_DO_USUARIO.en.md).

### Silent installation (Windows, administrators)

```powershell
# Setup.exe: Visual C++ (if missing) + program for all users
MeuEditorDeVideo-1.4.0-Setup.exe /quiet /norestart

# MSI for all users
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=1

# MSI for the current user only (no administrator)
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1

# Without the desktop shortcut
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /qn ALLUSERS=1 ADDLOCAL=Aplicativo

# With a detailed installation log
msiexec /i MeuEditorDeVideo-1.4.0-x64.msi /l*v install.log
```

### Microsoft Store

The program can also be distributed through the Microsoft Store (MSIX package). In that case Microsoft signs the
package and the SmartScreen warning does not appear. See
[`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md) (in Portuguese).

### Updating

Use **Help › Check for updates**. If the option is enabled, the program also checks on its own every 30 days (sending
only the interface language and the operating system). To update, install the new version over the current one
(Windows installer, `sudo apt install ./…deb` on Linux, or replacing the app in **Applications** on macOS): projects,
settings and AI models are kept.

### Uninstalling

**Linux:** `sudo apt remove meu-editor-de-video` (the package keeps no data of yours). **macOS:** drag the app from
**Applications** to the Trash. User data stays in both cases; to remove it, delete the user folders listed below (on
Linux, `~/.config/MeuEditorDeVideo` and `~/.local/share/MeuEditorDeVideo`; on macOS,
`~/Library/Application Support/MeuEditorDeVideo`; **Help › Open logs folder** shows where the program keeps its data
on your system).

**Windows:** from Windows, in **Settings › Apps › Meu Editor de Vídeo › Uninstall**, or with:

```powershell
msiexec /x MeuEditorDeVideo-1.4.0-x64.msi
```

Uninstalling removes the program files, the shortcuts, the `.med` association and the registry preferences
(`HKCU\Software\Meu Editor de Vídeo`). **User data is kept**:

- `%APPDATA%\MeuEditorDeVideo`: settings and recent projects;
- `%LOCALAPPDATA%\MeuEditorDeVideo`: downloaded AI models, autosaves, cache and render queue;
- your `.med` projects and media, wherever you saved them.

To remove everything, also delete those two folders. To clear only the cache before uninstalling, use
**Tools › Settings… › Cache › Clear cache**.

More details (in Portuguese): [`documentation/INSTALADOR.md`](documentation/INSTALADOR.md).

---

## Setting up the development environment

### Prerequisites

The same code base builds on **Windows 11 x64**, **Linux x64** (Debian 12+, Ubuntu 22.04+) and **macOS** (arm64 and
x64). Linux and macOS are validated by the CI only; the steps below mark what changes per system.

| Tool | Purpose |
|---|---|
| Windows 11 x64, Debian 12+/Ubuntu 22.04+ x64 or macOS 12+ | Operating system to build on |
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.4xx) | Build and test |
| [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) (`pwsh`) | Dependency, installer and check scripts |
| [Git](https://git-scm.com/downloads) | Clone the repository |
| Xcode Command Line Tools and `brew install cmake pkg-config nasm` (macOS only) | `fetch-deps` builds FFmpeg from source on macOS (`nasm` only on Intel) |
| `dpkg-deb` (Linux, optional) | `build-deb.ps1`; without it the script writes the `.deb` itself |
| Visual Studio 2026 or VS Code with C# Dev Kit (optional) | Editing and debugging |
| Python 3 (optional) | `scripts/extract-strings.py`, when adding or changing visible text |
| Windows SDK (optional) | `makeappx`, `makepri` and `signtool`, to build the MSIX package |

The WiX Toolset (MSI and Setup.exe installers) comes from NuGet and does not need to be installed.

### 1. Clone the repository

```powershell
git clone https://github.com/DiegoMendesRodrigues/Meu-Editor-de-Video.git
cd Meu-Editor-de-Video
```

### 2. Download the native dependencies

```powershell
pwsh scripts/fetch-deps.ps1
```

Fills `deps/` with the native libraries of the system it runs on, verifying each file against the SHA-256 recorded in
`scripts/deps.lock.json`:

- **Windows:** FFmpeg, the MODNet model and the Visual C++ Redistributable.
- **Linux:** the shared FFmpeg build (BtbN) and the MODNet model.
- **macOS:** builds FFmpeg from source with `scripts/macos/build-ffmpeg.sh` (it takes a while; needs the tools in the
  table above) and downloads the MODNet model.

Options: `-Force` downloads again; `-Strict` fails if any dependency has no recorded SHA-256. On Linux and macOS the
layout is `deps/ffmpeg/{bin,lib}`.

### 3. Build

```powershell
dotnet build MeuEditorDeVideo.sln
```

The project uses `TreatWarningsAsErrors`: the build must finish with **0 warnings**. Package versions live in
`Directory.Packages.props`, and the program version is the single `<Version>` in `Directory.Build.props`.

### 4. Test

```powershell
# Everything
dotnet test MeuEditorDeVideo.sln

# A single class
dotnet test tests/MeuEditorDeVideo.UnitTests --filter "FullyQualifiedName~RecentProjectRemovalTests"

# Architecture rules (layering and privacy)
dotnet test MeuEditorDeVideo.sln --filter "FullyQualifiedName~Architecture"

# Performance tests
dotnet test tests/MeuEditorDeVideo.MediaTests --filter "Categoria=Desempenho"
```

- `MediaTests` and `IntegrationTests` need `deps\ffmpeg` (step 2). They generate test media with `ffmpeg.exe` in a
  temporary folder whose path contains accents.
- AI tests are skipped when the model files are missing. Models are looked up in `%MED_MODELS_DIR%`, in
  `deps\ai-models` and among the models installed by the program.
- `PrivacyTests` fail if any assembly other than `ModelDownloads` uses `System.Net.*`.

### 5. Run

```powershell
dotnet run --project src/MeuEditorDeVideo.Desktop
```

The executable is at `src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo.exe` (`MeuEditorDeVideo`, with no
extension, on Linux and macOS).

### Environment variables

| Variable | Effect |
|---|---|
| `MED_FFMPEG_DIR` | Folder with the FFmpeg libraries, instead of `deps/ffmpeg` |
| `MED_MODELS_DIR` | Folder with the AI models used by the tests |
| `MED_AUDIO_BACKEND` | `sdl` uses SDL3 for audio on Windows too (the default there is WASAPI); Linux and macOS always use SDL3 |

### Building the installers and other tasks

```powershell
# MSI (installer/Msi) + Setup.exe (installer/Bundle) into artifacts/installer
pwsh scripts/build-installer.ps1

# MSIX package for the Microsoft Store into artifacts/store (-TestSign to test the installation locally)
pwsh scripts/build-msix.ps1

# Regenerate THIRD-PARTY-NOTICES.txt after changing any dependency
pwsh scripts/generate-notices.ps1

# Check, with the program running, that it opens no network connections
pwsh scripts/offline-check.ps1

# Extract visible text into resources/i18n/origem.json (then translate it in the five .tsv files)
python scripts/extract-strings.py
```

`build-installer.ps1` supports code signing with `-CertificateThumbprint`, or `-CertificatePath` and
`-CertificatePassword` (plus `-TimestampUrl` and `-SignToolPath`). See
[`documentation/INSTALADOR.md`](documentation/INSTALADOR.md) (in Portuguese).

### Packages for Linux and macOS

The CI (`.github/workflows/build.yml`) runs on `windows-latest`, `ubuntu-22.04`, `macos-15` (arm64) and `macos-15-intel`
(x64): it builds, runs the whole suite and, on pushes to `main`, builds the packages and installs them in a clean
environment (installers on Windows and macOS, `.deb` in Debian 12, Ubuntu 22.04 and 24.04 containers, one step per
image with its own 20-minute limit, through `scripts/ci/testar-deb-conteiner.sh`; `apt` retries when a mirror fails).

Both scripts publish the self-contained program for the target OS (`dotnet publish -r <rid> --self-contained`) and package
the result. Run `pwsh scripts/fetch-deps.ps1` first on the same OS (it fills `deps/` with that OS's native libraries).

```powershell
# Debian/Ubuntu package (.deb) into artifacts/linux, with SHA256SUMS.txt
pwsh scripts/build-deb.ps1 -Maintainer "Name <email>"   # or set MED_DEB_MAINTAINER

# macOS app (Meu Editor de Vídeo.app) and disk image (.dmg) into artifacts/macos, one run per architecture
pwsh scripts/build-macos.ps1 -Rid osx-arm64      # Apple silicon
pwsh scripts/build-macos.ps1 -Rid osx-x64        # Intel
```

**Linux `.deb`** (Debian 12+, Ubuntu 22.04+, x64). The package installs the whole publish folder in
`/opt/meu-editor-de-video/`, the `meu-editor-de-video` launcher in `/usr/bin`, the menu entry, the `.med` file type
and the icons. It uses `dpkg-deb --root-owner-group` when available (any Debian/Ubuntu machine, or `apt install dpkg`);
otherwise the script writes the `.deb` itself, which also works on Windows and macOS for testing the layout. The
dependency list is at the top of `scripts/build-deb.ps1` (see `documentation/DEPENDENCIAS.md`). Install with
`sudo apt install ./meu-editor-de-video_<version>_amd64.deb`.

**macOS `.app` / `.dmg`**. The bundle puts the whole publish folder in `Contents/MacOS`, with `Info.plist`, the icon
(`.icns`) and the permission texts for camera and microphone in six languages. The `.med` type is declared in
`Info.plist`. Screen recording has no `Info.plist` key: macOS asks for it on the first recording (System Settings ›
Privacy & Security › Screen Recording). The `.dmg` needs macOS (`hdiutil`); on other systems the script only builds
the `.app` folder and a `.zip` to inspect its structure, which is not a usable app (no `codesign`, no `plutil`).

Signing and notarization are optional and read their settings from environment variables, never from the repository:

| Variable | Used by | Meaning |
|---|---|---|
| `MED_MACOS_SIGN_IDENTITY` | `-Sign` | Certificate name, e.g. `Developer ID Application: Name (TEAMID)` (must be in the keychain) |
| `MED_MACOS_NOTARY_PROFILE` | `-Notarize` | Profile created with `xcrun notarytool store-credentials` |
| `MED_MACOS_NOTARY_KEYCHAIN` | `-Notarize` | Optional: keychain file that holds that profile, when it is not the default |

```bash
export MED_MACOS_SIGN_IDENTITY="Developer ID Application: Name (TEAMID)"
export MED_MACOS_NOTARY_PROFILE="med-notary"
pwsh scripts/build-macos.ps1 -Rid osx-arm64 -Sign -Notarize
```

`-Sign` signs every native library and the `ffmpeg` tool, then the app, with the hardened runtime and
`packaging/macos/MeuEditorDeVideo.entitlements`. `-Notarize` submits the app and the `.dmg` to Apple and staples the
tickets. Without them, the app only gets an ad hoc signature and macOS Gatekeeper blocks the first launch: allow it in
System Settings › Privacy & Security › Open Anyway (on macOS 14 and earlier, right-click › Open). The CI
(`.github/workflows/build.yml`) builds all four targets and, on pushes to `main`, produces these packages; it signs
and notarizes when the repository secrets `MACOS_SIGN_IDENTITY`, `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD`,
`APPLE_ID`, `APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` exist.

### Things to watch when testing the real program

- While the program is open on Windows, the DLLs in `bin\` are locked and `dotnet build`/`dotnet test` fail with
  MSB3027. Close the program first, or build into a separate folder with `-o` just to check that it compiles.
- The program uses your real data: `%APPDATA%\MeuEditorDeVideo\settings.json` (recent projects),
  `%LOCALAPPDATA%\MeuEditorDeVideo\` (autosave, cache, AI models and render queue) and the preferences in
  `HKCU\Software\Meu Editor de Vídeo`. Back them up before testing. To test a first run (welcome window), empty that
  registry key. On Linux and macOS the same data lives in the user folders (`~/.config/MeuEditorDeVideo` and
  `~/.local/share/MeuEditorDeVideo` on Linux; `~/Library/Application Support/MeuEditorDeVideo` on macOS), and the
  preferences are the `preferences.json` file in the settings folder: delete it for a first run.
- `scripts/ci/testar-instalador.ps1` and `scripts/ci/testar-deb.sh` install the app and **wipe its user data**: they are
  for the CI or a disposable container only.
- `pwsh scripts/offline-check.ps1` also works on Linux (`ss`) and macOS (`lsof`).

### Further reading (in Portuguese)

- Architecture and recipes for extending the program (effects, transitions, export templates, AI models, shortcuts
  and `.med` migrations): [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Dependencies and licenses: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md)
- AI models: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
