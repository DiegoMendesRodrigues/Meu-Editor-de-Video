**English** | [Português (Brasil)](INSTALL.pt-BR.md)

# Installation

This guide has two parts:

1. [Installing the application (end users)](#installing-the-application-end-users)
2. [Setting up the development environment](#setting-up-the-development-environment)

---

## Installing the application (end users)

### System requirements

| | Minimum | Recommended |
|---|---|---|
| Operating system | Windows 11 64-bit (x64) | Windows 11 64-bit (x64) |
| Processor | Intel Core i5 or AMD Ryzen 5 | Intel Core i7 or AMD Ryzen 7 |
| Memory | 16 GB | 32 GB |
| Graphics card | Direct3D compatible | NVIDIA RTX or AMD Radeon with 8 GB |
| Storage | SSD | 1 TB NVMe SSD |

The program takes about 540 MB once installed. .NET is bundled, so there is nothing else to install.

### Which installer to download

The installers are published on the repository's **Releases** page on GitHub, whenever a version is available.

| File | When to use it |
|---|---|
| `MeuEditorDeVideo-<version>-Setup.exe` | **Recommended.** Installs the Visual C++ Redistributable x64 if it is missing, then the program for all users in `C:\Program Files\Meu Editor de Vídeo` (asks for administrator permission). |
| `MeuEditorDeVideo-<version>-x64.msi` | For computers that already have Visual C++ 2015-2026 (14.40 or newer) and for company deployment (GPO, Intune). Lets you choose **just for me** (no administrator, in `%LOCALAPPDATA%\Apps\Meu Editor de Vídeo`) or **for all users**. |
| `SHA256SUMS.txt` | SHA-256 checksums to verify the downloaded files. |

To verify a downloaded file, in PowerShell:

```powershell
Get-FileHash .\MeuEditorDeVideo-1.1.0-Setup.exe -Algorithm SHA256
```

The result must match the corresponding line in `SHA256SUMS.txt`.

### Installing

1. Run `Setup.exe` (or the `.msi`) and follow the screens.
2. The installers are not code-signed, so on the first download Windows SmartScreen may show "Windows protected your
   PC". Click **More info › Run anyway**. Installation and operation are not affected.
3. Open the program from the Start menu or the desktop shortcut. `.med` files now open in the editor.
4. On the first run, the **welcome** window asks for your name, language, theme and whether the program should check
   for updates.

### Optional AI models

Background removal (MODNet) comes preinstalled. The other models (Whisper subtitles, voice isolation, scene detection,
face retouching, the Phi-4-mini virtual assistant and more) are installed by the program itself, in
**Tools › AI Models… › Download models…**, with the **Install** button. Each model is downloaded only once from its official source
and verified with SHA-256. Details in the [user manual](documentation/MANUAL_DO_USUARIO.en.md).

### Silent installation (administrators)

```powershell
# Setup.exe: Visual C++ (if missing) + program for all users
MeuEditorDeVideo-1.1.0-Setup.exe /quiet /norestart

# MSI for all users
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=1

# MSI for the current user only (no administrator)
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=2 MSIINSTALLPERUSER=1

# Without the desktop shortcut
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /qn ALLUSERS=1 ADDLOCAL=Aplicativo

# With a detailed installation log
msiexec /i MeuEditorDeVideo-1.1.0-x64.msi /l*v install.log
```

### Microsoft Store

The program can also be distributed through the Microsoft Store (MSIX package). In that case Microsoft signs the
package and the SmartScreen warning does not appear. See
[`documentation/MICROSOFT_STORE.md`](documentation/MICROSOFT_STORE.md) (in Portuguese).

### Updating

Use **Help › Check for updates**. If the option is enabled, the program also checks on its own every 30 days (sending
only the interface language and the operating system). To update, install the new version over the current one: projects, settings and AI
models are kept.

### Uninstalling

From Windows, in **Settings › Apps › Meu Editor de Vídeo › Uninstall**, or with:

```powershell
msiexec /x MeuEditorDeVideo-1.1.0-x64.msi
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

| Tool | Purpose |
|---|---|
| Windows 11 x64 | The only supported OS (Avalonia, DirectML, WASAPI) |
| [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.4xx) | Build and test |
| [PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows) (`pwsh`) | Dependency, installer and check scripts |
| [Git](https://git-scm.com/download/win) | Clone the repository |
| Visual Studio 2026 or VS Code with C# Dev Kit (optional) | Editing and debugging |
| Python 3 (optional) | `scripts/extract-strings.py`, when adding or changing visible text |
| Windows SDK (optional) | `makeappx`, `makepri` and `signtool`, to build the MSIX package |

The WiX Toolset (MSI and Setup.exe installers) comes from NuGet and does not need to be installed.

### 1. Clone the repository

```powershell
git clone https://github.com/DiegoMendesRodrigues/Meu-Editor-de-V-deo.git
cd Meu-Editor-de-V-deo
```

### 2. Download the native dependencies

```powershell
pwsh scripts/fetch-deps.ps1
```

Downloads FFmpeg, the MODNet model and the Visual C++ Redistributable into `deps/`, verifying each file against the
SHA-256 recorded in `scripts/deps.lock.json`. Options: `-Force` downloads again; `-Strict` fails if any dependency
has no recorded SHA-256.

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

The executable is at `src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo.exe`.

### Environment variables

| Variable | Effect |
|---|---|
| `MED_FFMPEG_DIR` | Folder with the FFmpeg DLLs, instead of `deps\ffmpeg` |
| `MED_MODELS_DIR` | Folder with the AI models used by the tests |

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
tickets. Without them, the `.dmg` is unsigned and macOS Gatekeeper asks for confirmation (right-click › Open). The CI
(`.github/workflows/build.yml`) builds all four targets and, on pushes to `main`, produces these packages; it signs
and notarizes when the repository secrets `MACOS_SIGN_IDENTITY`, `MACOS_CERT_P12_BASE64`, `MACOS_CERT_PASSWORD`,
`APPLE_ID`, `APPLE_TEAM_ID` and `APPLE_APP_PASSWORD` exist.

### Things to watch when testing the real program

- While the program is open, the DLLs in `bin\` are locked and `dotnet build`/`dotnet test` fail with MSB3027. Close
  the program first, or build into a separate folder with `-o` just to check that it compiles.
- The program uses your real data: `%APPDATA%\MeuEditorDeVideo\settings.json` (recent projects),
  `%LOCALAPPDATA%\MeuEditorDeVideo\` (autosave, cache, AI models and render queue) and the preferences in
  `HKCU\Software\Meu Editor de Vídeo`. Back them up before testing. To test a first run (welcome window), empty that
  registry key.

### Further reading (in Portuguese)

- Architecture and recipes for extending the program (effects, transitions, export templates, AI models, shortcuts
  and `.med` migrations): [`documentation/DOCUMENTACAO_TECNICA.md`](documentation/DOCUMENTACAO_TECNICA.md)
- Dependencies and licenses: [`documentation/DEPENDENCIAS.md`](documentation/DEPENDENCIAS.md)
- AI models: [`documentation/MODELOS_IA.md`](documentation/MODELOS_IA.md)
