# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Meu Editor de Vídeo** — non-linear video editor for Windows 11 x64, Linux x64 (Debian 12+, Ubuntu 22.04+) and macOS (arm64, x64), from one code base. Linux and macOS are validated by CI only (no local VM). C#/.NET 10, Avalonia 12 with the Fluent theme, MVVM via CommunityToolkit.Mvvm, DI via Microsoft.Extensions.Hosting. FFmpeg (via FFmpeg.AutoGen), ONNX Runtime (DirectML/CPU), Whisper.net, NAudio (WASAPI) / SDL3 audio, SkiaSharp + HarfBuzz text, OpenCvSharp. License GPL-3.0-or-later (`LICENSE.md`; the build copies it as `LICENSE.txt` next to the exe, and `build-installer.ps1` / `generate-notices.ps1` read it).

- UI text, docs (`documentation/`), code comments, and commit messages are in **Portuguese (pt-BR)**.
- Spec: `ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip` (RF/RNF/CA ids referenced in code and comments).
- Architecture reference: `documentation/DOCUMENTACAO_TECNICA.md`. It includes "Como estender" recipes for new effects, transitions, export templates, AI models, shortcuts, and `.med` migrations. Follow them.

## Commands

```powershell
pwsh scripts/fetch-deps.ps1                       # deps/ for the host OS (SHA-256 checked against scripts/deps.lock.json): Windows FFmpeg+MODNet+VC++; Linux FFmpeg (BtbN shared); macOS builds FFmpeg from source (scripts/macos/build-ffmpeg.sh, needs Xcode CLT + brew cmake pkg-config nasm)
dotnet build MeuEditorDeVideo.sln                 # TreatWarningsAsErrors: must build with 0 warnings
dotnet test MeuEditorDeVideo.sln
dotnet test tests/MeuEditorDeVideo.UnitTests --filter "FullyQualifiedName~RecentProjectRemovalTests"   # single class
dotnet test MeuEditorDeVideo.sln --filter "FullyQualifiedName~Architecture"
dotnet test tests/MeuEditorDeVideo.MediaTests --filter "Categoria=Desempenho"
dotnet run --project src/MeuEditorDeVideo.Desktop # exe: src/MeuEditorDeVideo.Desktop/bin/Debug/net10.0/MeuEditorDeVideo.exe
pwsh scripts/build-installer.ps1                  # publish + WiX MSI (installer/Msi) + Burn Setup.exe (installer/Bundle) into artifacts/installer
pwsh scripts/build-msix.ps1                      # MSIX for the Microsoft Store into artifacts/store (-TestSign for a local install test); see documentation/MICROSOFT_STORE.md
pwsh scripts/generate-notices.ps1                 # regenerate THIRD-PARTY-NOTICES.txt after any dependency change (GPL text comes from LICENSE.md)
pwsh scripts/build-deb.ps1 -Maintainer "Name <email>"  # Linux .deb into artifacts/linux (or env MED_DEB_MAINTAINER)
pwsh scripts/build-macos.ps1                      # macOS .app/.dmg into artifacts/macos (-Sign/-Notarize via MED_MACOS_* env vars)
pwsh scripts/offline-check.ps1                    # runtime check that the app opens no network connections (Windows, Linux `ss`, macOS `lsof`)
```

- MediaTests and IntegrationTests need `deps\ffmpeg` (run `fetch-deps` first). They generate test media with `ffmpeg` in a temp folder whose path has accents. On Linux/macOS the layout is `deps/ffmpeg/{bin,lib}` (the ffmpeg binary finds its libraries via RPATH `$ORIGIN/../lib`; `src/AppContent.props` copies both folders).
- AI tests (`ModelFact`/`ModeloFact`, `WhisperFact`) are skipped when their model files are absent (`%MED_MODELS_DIR%`, `deps\ai-models`, installed models).
- IntegrationTests run sequentially and use `Support/AppContainer.cs`, which mirrors the app's DI composition (`AddMeuEditor`) without a window.
- Package versions are central in `Directory.Packages.props`. App version is the single `<Version>` in `Directory.Build.props`.
- Cross-platform: OS-specific code stays behind `OperatingSystem.Is…()` in `net10.0` projects (recipe "Recurso que depende do sistema operacional" in `DOCUMENTACAO_TECNICA.md`). User-visible OS differences (install, Cmd on macOS, Wayland limits) are in the manuals. Packages with per-OS native assets are conditioned on `MedPlataforma` (`windows`/`linux`/`macos`, from `-r <rid>` or the build host; `Directory.Build.props`). Check another OS's restore with `-p:MedPlataforma=linux` in a scratch copy, not in the repo's `obj/`. Projects without Windows APIs target `net10.0`; keep them that way. OpenCvSharp on macOS uses the `OpenCvSharp4.runtime.osx.*` packages.
- CI (`.github/workflows/build.yml`): win-x64, linux-x64 (ubuntu-22.04), osx-arm64 (macos-15), osx-x64 (macos-15-intel); packages are built on push to main. The `.deb` step needs the repository variable `MED_DEB_MAINTAINER`; macOS signing uses the `MACOS_*`/`APPLE_*` secrets.

## Architecture

Layered solution, 14 projects in `src/`. The layering is **enforced by NetArchTest tests** in `tests/MeuEditorDeVideo.UnitTests/Architecture/`.

- `Domain`: immutable records (`Project`, `Sequence`, `Track`, `Clip`, `MediaAsset`…). No package references.
  - Time is `MediaTime`, a `long` in **flicks** (705,600,000 per second). Never use `double` for model time.
  - Edits are `IEditCommand`s (`Project Apply(Project)`, returning a new state) recorded in `EditHistory` (full-state snapshots; `MergeKey` coalesces slider drags).
  - Business-rule violations throw `EditRejectedException`, with a pt-BR message shown to the user.
- `Core`: interfaces, DTOs, `MedError`/`MedErrors` (user-facing error codes such as `MED-3003`). Depends only on `Domain`.
- `Application`: use cases (project open/save/recover, import, render queue, autosave). It sees **only `Core` interfaces**, never engines, `Infrastructure`, or the UI toolkit.
- `Infrastructure`: settings JSON (`%APPDATA%\MeuEditorDeVideo\settings.json`), paths, Serilog, cache, hardware detection.
- Engines: `ProjectEngine` (`.med` format), `TimelineEngine` (evaluate the timeline at time t, snapping), `MediaEngine` (FFmpeg), `ImageEngine` (effects, text, masks, tracking), `AudioEngine` (mix, DSP, WASAPI), `AIEngine` (`.medmodel` packages, ONNX sessions), `RenderingEngine` (compositor, `PreviewEngine`, `RenderEngine` export).
  - Engines must not reference `Desktop`, `Presentation` or `Application`.
  - Each engine exposes an `Add…` DI extension method.
- `Presentation` (`net10.0`): view models, `IDialogService`, `INavigationService`, `ShortcutService`, `ProjectWorkflow`, `Translator`. No WPF/Avalonia (`LayeringTests`): keys are `MedKey`/`MedModifiers` (same names and values as WPF `Key`/`ModifierKeys`; `App/Controls/KeyMapping.cs` casts, `AppTests` checks), colors are `ArgbColor` or `"#AARRGGBB"` strings, points `PointD`, preview frames `DisplayFrame` (shown by the App's `FrameImage.Frame`), thumbnails `BgraImage` (`BgraBitmaps` converter). The UI thread is `UiThread.Dispatcher` (`IUiDispatcher`; the App sets `WpfUiDispatcher` at startup; tests run `Post` inline and timers never fire). Opening files/URLs goes through `SystemShell`.
- `Presentation/Hosting`: `EditorComposition.AddMeuEditor` is the shared DI composition (engines, use cases, view models); `EditorStartup` holds the toolkit-independent startup/restart steps.
- `Desktop` (Avalonia, `net10.0`, assembly/exe `MeuEditorDeVideo`): views, controls and dialogs only. `App.axaml.cs` (`ConfigureServices`) calls `AddMeuEditor` and adds only `IThemeService`, `IDialogService` and `MainWindow`. Implementations are bound to `Core` interfaces only in these two places. Tests: `tests/MeuEditorDeVideo.DesktopTests` (Avalonia.Headless; `Support/Ui.cs` runs and captures windows).
- `AIEngine` vision services (`Vision/`): `SegmentationService` (MODNet person matte), `CameraTracker` (head/body auto-framing), `FaceLandmarkService` + `FaceMeshDetector` (MediaPipe Face Mesh V2, 478 points) used by `FaceRetouch` (effects `ai.beauty`, `ai.makeup`). Optional models are listed in `resources/ai-models/modelos.json` (pinned URL + SHA-256, license text in `resources/ai-models/licencas/`) and installed by the user from the app; don't copy them into `deps\ai-models`.
- Virtual assistant (Ajuda › Assistente virtual, Shift+F1): `Application/Help/ManualKnowledgeBase` (manual sections + BM25 search, no model) and `HelpAssistantService` (prompt in the UI language); `AIEngine/Assistant/GenAiAnswerGenerator` runs the optional `HelpAssistant` model (Phi-4-mini ONNX int4, ONNX Runtime GenAI, DirectML). Catalog entries can have `extraFiles` (multi-file models). The six manuals must keep identical heading lines (`ManualKnowledgeBaseTests`).
- `ModelDownloads`: the **only** assembly allowed to use the network (HTTP to pinned URLs with SHA-256, only when the user clicks Instalar; plus `HttpUpdateChecker` for Ajuda › Verificar atualizações and the 30-day startup check, see `Core/Updates/UpdateEndpoints`; the request sends only `language` and `os`). `PrivacyTests` fails the build if any other product assembly touches `System.Net.*`. Keep it that way. A new product assembly must be added to `ProductAssemblies` in those tests.

### `.med` project format (ProjectEngine)

- ZIP container with `manifest.json` and `project.json`.
- Saves are atomic (temp file, then verify, then replace).
- Versioned by `MedMigrator.Current`. Migrations in `Format/MedMigrations.cs` operate on raw JSON.
- New `Domain` fields need neutral defaults, or old files won't load.
- Persisted identifiers must never be renamed: effect type strings in `EffectCatalog`, `TransitionKind` values, shortcut ids in `ShortcutCatalog`.

### Preview and export pipeline

- `TimelineEvaluator`: visible layers at time t.
- `FrameRenderer`: decodes frames through `IVideoFrameSource` (proxy when available), applies the effect stack (`EffectProcessor` interleaved with `IAiFrameProcessor`), and rasterizes text.
- `CpuCompositor`: composites the layers (2.5D perspective).
- `PreviewEngine`: has a render thread and an audio thread. The WASAPI playback position is the master clock.
- `RenderEngine`: drives export through the same path with FFmpeg encoding (NVENC/AMF/QSV/software).

### Timeline editing commands (`Domain/Editing/TimelineCommands.cs`)

- `SplitClipsCommand` (Dividir) and `JoinClipsCommand` (Juntar os cortes): a join only merges neighbours that continue the same media (`CanJoin`: contiguous source, identical settings); `FindCut` locates a joinable cut near the playhead.
- `RemoveClipsCommand`: `Ripple` closes the gap on the clip's track (Excluir e fechar o espaço); `AllTracks` closes it on every unlocked track (Excluir e juntar) and is rejected if another track would overlap.

### Desktop (Avalonia) conventions

- **Navigation:** pages are `PageViewModel`s mapped to views by `DataTemplate`s in `App.axaml` (inspector panels: `Views/Panels/InspectorTemplateSelector`). `INavigationService.NavigateTo<T>()` switches pages; `OnNavigatedTo()` refreshes state.
- **Dialogs:** view models never create windows: they call a typed `IDialogService` method (interface in `Presentation/Services/IDialogService.cs`, implementation in `Desktop/Services/DialogService.cs`), so they stay testable with fakes. Modal dialogs are synchronous through `DialogHost.ShowModal` (`ModalLoop`).
  - Adding a method means updating the fakes in `tests/MeuEditorDeVideo.UnitTests/Application/*Tests.cs` and `tests/MeuEditorDeVideo.DesktopTests/Support/*Fakes.cs`.
  - Simple custom dialogs are code-only `Window`s in `Views/Dialogs/` (e.g. `PromptWindow`).
- **Strings:** `Resources/Strings/{Common,Editor}.axaml` (`sys:String` keys like `Editor.NaoSalvo`). Theme brushes are `DynamicResource` keys `Med.*` (`Med.Text`, `Med.Background`, `Med.Panel`, `Med.Accent`, `Med.Danger`…), defined per `ThemeVariant` in `Themes/Palette.axaml`; `ThemeService` sets `RequestedThemeVariant`. Shared style classes (`med-panel`, `med-section-title`, `med-muted`, `med-accent`, `med-toolbar`, `med-icon`) are in `Themes/Styles.axaml`.
- **Bindings are compiled** (`AvaloniaUseCompiledBindingsByDefault`): every view and `DataTemplate` needs `x:DataType`; a wrong path fails the build.
- **Icons:** font `Med Icons` (`resources/fonts/MedIcons.ttf`, from Fluent UI System Icons, same codepoints as Segoe Fluent Icons). A new icon needs an entry in `resources/fonts/icones.json` and a rebuilt font (`scripts/build-icon-font.py`); `BaseTests` fails if a codepoint used in Desktop/Presentation code has no glyph.
- **Title bar:** Avalonia draws its own title text when the client area is extended; `MainWindow.HideDrawnTitle` hides it (app styles don't reach that overlay).
- **Styling:** don't apply broad selectors (e.g. every `Control`) just for margins; set `Margin` directly or use a class.
- **Editor window** (`Views/EditorView.axaml` + code-behind): menus Arquivo/Editar/Visualizar/Projeto/Ferramentas/Ajuda bind to `Timeline.*` and `Preview.*` commands; menu shortcuts are shown via `ctl:ShortcutText.Action` (ids from `ShortcutService`). Visualizar › Modo do layout (`AplicarLayout`: Padrão, Linha do tempo, Vídeo) re-parents `PainelMonitor`/`PainelPropriedades` and changes grid spans in code; it must restore the default layout before applying another or maximizing the monitor. Monitor zoom, playback quality, loop (`PreviewViewModel.LoopPlayback`) and the pixel ruler (`Controls/MonitorRuler.cs`) live there too. Ajuda › Verificar atualizações calls `Presentation/Services/UpdateCheckService` (network code stays in `ModelDownloads`). On macOS the menu becomes the native menu bar (`Services/NativeMenuBuilder`).
- **Startup:** there is no home screen. The app opens the editor with an untitled project (after offering autosave recovery); File lists the last 5 recent projects (`MenuArquivo_SubmenuOpened`).
- **Languages:** pt-BR source text is the translation key. `resources/i18n/origem.json` (from `scripts/extract-strings.py`) plus `<lang>.tsv` files (`id<TAB>text`, id = first 8 hex of SHA-256 of the pt-BR text) for `en`, `es`, `fr`, `de`, `pt-PT`. `Presentation/Localization/Translator` + `Desktop/Localization/LocalizationHook` translate the visual tree at load time (the extractor also scans `.axaml`, `ToolTip.Tip` and `Watermark`); code-built messages go through `Translator.T`/`F`. When adding visible text, re-run the extractor and add the five translations (`TranslatorTests` checks ids and `{n}` markers). The user manual has one file per language (`MANUAL_DO_USUARIO.<lang>.md`). Saving a different language in Settings restarts the app (`App.RestartForLanguage`), since translation happens once at load.
- **Welcome window:** if any preference is missing or empty (`UserPreferences.IsComplete`, read before the host is built), `App.AskPreferencesAsync` shows `Views/Dialogs/WelcomeWindow` before the main window; it cannot be closed without saving. Testing a first run means emptying the registry key (Windows): back it up first.
- **Preferences:** on Windows `HKCU\Software\Meu Editor de Vídeo` (`Name`, `Language`, `ThemeColor`, `CheckForNewVersions`, `LastCheck`) via `IUserPreferencesStore`/`RegistryPreferencesStore`; on Linux/macOS a JSON file (`FilePreferencesStore`). They override `settings.json`. Testing the real app also touches this key; back it up too.
- Controls need `AutomationProperties.Name` (accessibility; also used by UI Automation testing).
- **Global F10** (stop screen recording): `IGlobalHotkey` via `GlobalHotkeys.Current`, implemented with SharpHook (`Services/SharpHookGlobalHotkey.cs`); under Wayland it only works with the window focused, on macOS it needs the Accessibility permission.
- `BuiltInComInteropSupport` must stay `true` in the Desktop csproj: NAudio (WASAPI) and SAPI speech use COM (`RuntimeConfigTests`).
- The user manual `documentation/MANUAL_DO_USUARIO.md` ships with the app (Ajuda › Manual, F1). Update it when user-visible behavior changes.

## Working on this machine

- Testing the real app touches the user's data: `%APPDATA%\MeuEditorDeVideo\settings.json` (recent projects) and `%LOCALAPPDATA%\MeuEditorDeVideo\` (autosave, cache, installed AI models, render queue).
  - Back up `settings.json` before driving the app and restore it afterwards.
  - Remove only test-created autosave and project files.
- While the app is running, its `bin\` DLLs are locked and `dotnet build`/`dotnet test` fail with MSB3027. Ask before closing a user's instance; to only check compilation, build into a scratch folder with `-o` (but run the full test suite only with the normal output, since `ExportTemplateStoreTests` fail when outputs are mixed).
- Drive the app through UI Automation (`AutomationProperties.Name`) or Win32 `SendMessage`; don't use `SendKeys` (keys may go to another window).
- When capturing screenshots of the app, capture only its window (e.g. `PrintWindow`), never the full screen.
