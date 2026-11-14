# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Meu Editor de Vídeo** — non-linear video editor for Windows 11 x64. C#/.NET 10, WPF with the Fluent theme (`ThemeMode`), MVVM via CommunityToolkit.Mvvm, DI via Microsoft.Extensions.Hosting. FFmpeg (via FFmpeg.AutoGen), ONNX Runtime (DirectML/CPU), Whisper.net, NAudio (WASAPI), OpenCvSharp. License GPL-3.0-or-later (`LICENSE.md`; the build copies it as `LICENSE.txt` next to the exe, and `build-installer.ps1` / `generate-notices.ps1` read it).

- UI text, docs (`documentation/`), code comments, and commit messages are in **Portuguese (pt-BR)**.
- Spec: `ESPECIFICACAO_TECNICA_E_FUNCIONAL.zip` (RF/RNF/CA ids referenced in code and comments).
- Architecture reference: `documentation/DOCUMENTACAO_TECNICA.md`. It includes "Como estender" recipes for new effects, transitions, export templates, AI models, shortcuts, and `.med` migrations. Follow them.

## Commands

```powershell
pwsh scripts/fetch-deps.ps1                       # FFmpeg, MODNet, VC++ redist into deps/ (SHA-256 checked against scripts/deps.lock.json)
dotnet build MeuEditorDeVideo.sln                 # TreatWarningsAsErrors: must build with 0 warnings
dotnet test MeuEditorDeVideo.sln
dotnet test tests/MeuEditorDeVideo.UnitTests --filter "FullyQualifiedName~RecentProjectRemovalTests"   # single class
dotnet test MeuEditorDeVideo.sln --filter "FullyQualifiedName~Architecture"
dotnet test tests/MeuEditorDeVideo.MediaTests --filter "Categoria=Desempenho"
dotnet run --project src/MeuEditorDeVideo.App     # exe: src/MeuEditorDeVideo.App/bin/Debug/net10.0-windows/win-x64/MeuEditorDeVideo.exe
pwsh scripts/build-installer.ps1                  # publish + WiX MSI (installer/Msi) + Burn Setup.exe (installer/Bundle) into artifacts/installer
pwsh scripts/build-msix.ps1                      # MSIX for the Microsoft Store into artifacts/store (-TestSign for a local install test); see documentation/MICROSOFT_STORE.md
pwsh scripts/generate-notices.ps1                 # regenerate THIRD-PARTY-NOTICES.txt after any dependency change (GPL text comes from LICENSE.md)
pwsh scripts/offline-check.ps1                    # runtime check that the app opens no network connections
```

- MediaTests and IntegrationTests need `deps\ffmpeg` (run `fetch-deps` first). They generate test media with `ffmpeg.exe` in a temp folder whose path has accents.
- AI tests (`ModelFact`/`ModeloFact`, `WhisperFact`) are skipped when their model files are absent (`%MED_MODELS_DIR%`, `deps\ai-models`, installed models).
- IntegrationTests run sequentially and use `Support/AppContainer.cs`, which mirrors the App's DI composition without a window.
- Package versions are central in `Directory.Packages.props`. App version is the single `<Version>` in `Directory.Build.props`.

## Architecture

Layered solution, 13 projects in `src/`. The layering is **enforced by NetArchTest tests** in `tests/MeuEditorDeVideo.UnitTests/Architecture/`.

- `Domain`: immutable records (`Project`, `Sequence`, `Track`, `Clip`, `MediaAsset`…). No package references.
  - Time is `MediaTime`, a `long` in **flicks** (705,600,000 per second). Never use `double` for model time.
  - Edits are `IEditCommand`s (`Project Apply(Project)`, returning a new state) recorded in `EditHistory` (full-state snapshots; `MergeKey` coalesces slider drags).
  - Business-rule violations throw `EditRejectedException`, with a pt-BR message shown to the user.
- `Core`: interfaces, DTOs, `MedError`/`MedErrors` (user-facing error codes such as `MED-3003`). Depends only on `Domain`.
- `Application`: use cases (project open/save/recover, import, render queue, autosave). It sees **only `Core` interfaces**, never engines, `Infrastructure`, or WPF.
- `Infrastructure`: settings JSON (`%APPDATA%\MeuEditorDeVideo\settings.json`), paths, Serilog, cache, hardware detection.
- Engines: `ProjectEngine` (`.med` format), `TimelineEngine` (evaluate the timeline at time t, snapping), `MediaEngine` (FFmpeg), `ImageEngine` (effects, text, masks, tracking), `AudioEngine` (mix, DSP, WASAPI), `AIEngine` (`.medmodel` packages, ONNX sessions), `RenderingEngine` (compositor, `PreviewEngine`, `RenderEngine` export).
  - Engines must not reference `App` or `Application`.
  - Each engine exposes an `Add…` DI extension method.
- `App`: WPF. **All DI composition lives in `App.xaml.cs`** (`ConfigureServices`). Implementations are bound to `Core` interfaces only here.
- `AIEngine` vision services (`Vision/`): `SegmentationService` (MODNet person matte), `CameraTracker` (head/body auto-framing), `FaceLandmarkService` + `FaceMeshDetector` (MediaPipe Face Mesh V2, 478 points) used by `FaceRetouch` (effects `ai.beauty`, `ai.makeup`). Optional models are listed in `resources/ai-models/modelos.json` (pinned URL + SHA-256, license text in `resources/ai-models/licencas/`) and installed by the user from the app; don't copy them into `deps\ai-models`.
- `ModelDownloads`: the **only** assembly allowed to use the network (HTTP to pinned URLs with SHA-256, only when the user clicks Instalar; plus `HttpUpdateChecker` for Ajuda › Verificar atualizações and the 30-day startup check, see `Core/Updates/UpdateEndpoints`). `PrivacyTests` fails the build if any other product assembly touches `System.Net.*`. Keep it that way. A new product assembly must be added to `ProductAssemblies` in those tests.

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

### App (WPF) conventions

- **Navigation:** pages are `PageViewModel`s mapped to views in `Views/ViewMappings.xaml`. `INavigationService.NavigateTo<T>()` switches pages; `OnNavigatedTo()` refreshes state.
- **Dialogs:** go through `IDialogService` (`Services/DialogService.cs`), so view models stay testable with fakes.
  - Adding a method means updating the fakes in `tests/MeuEditorDeVideo.UnitTests/Application/*Tests.cs`.
  - Simple custom dialogs are code-only `Window`s in `Views/Dialogs/` (e.g. `PromptWindow`).
- **Strings:** `Resources/Strings/{Common,Editor}.xaml` (`sys:String` keys like `Editor.NaoSalvo`). Theme brushes are `DynamicResource` keys `Med.*` (`Med.Text`, `Med.Background`, `Med.Panel`, `Med.Accent`, `Med.Danger`…), defined in `Themes/Palette.{Dark,Light}.xaml` and swapped by `ThemeService`.
- **Fluent styling pitfall:** a `Style` without `BasedOn="{StaticResource {x:Type X}}"` replaces the Fluent template with the classic Aero one. On dark windows this produces white-on-white dropdowns and lists. Never apply generic styles (e.g. `TargetType="FrameworkElement"` just for margins) to controls; set `Margin` directly. `ItemContainerStyle`s must be `BasedOn` the theme style.
- **Editor window** (`Views/EditorView.xaml` + code-behind): menus Arquivo/Editar/Visualizar/Projeto/Ferramentas/Ajuda bind to `Timeline.*` and `Preview.*` commands; menu shortcuts are shown via `ctl:ShortcutText.Action` (ids from `ShortcutService`). Visualizar › Modo do layout (`AplicarLayout`: Padrão, Linha do tempo, Vídeo) re-parents `PainelMonitor`/`PainelPropriedades` and changes grid spans in code; it must restore the default layout before applying another or maximizing the monitor. Monitor zoom, playback quality, loop (`PreviewViewModel.LoopPlayback`) and the pixel ruler (`Controls/MonitorRuler.cs`) live there too. Ajuda › Verificar atualizações calls `Services/UpdateCheckService` (network code stays in `ModelDownloads`).
- **Startup:** there is no home screen. The app opens the editor with an untitled project (after offering autosave recovery); File lists the last 5 recent projects (`MenuArquivo_SubmenuOpened`).
- **Languages:** pt-BR source text is the translation key. `resources/i18n/origem.json` (from `scripts/extract-strings.py`) plus `<lang>.tsv` files (`id<TAB>text`, id = first 8 hex of SHA-256 of the pt-BR text) for `en`, `es`, `fr`, `de`, `pt-PT`. `Localization/Translator` + `LocalizationHook` translate the WPF tree at load time; code-built messages go through `Translator.T`/`F`. When adding visible text, re-run the extractor and add the five translations (`TranslatorTests` checks ids and `{n}` markers). The user manual has one file per language (`MANUAL_DO_USUARIO.<lang>.md`). Saving a different language in Settings restarts the app (`App.RestartForLanguage`), since translation happens once at load.
- **Welcome window:** if any registry preference is missing or empty (`UserPreferences.IsComplete`, read before the host is built), `App.AskPreferencesAsync` shows `Views/Dialogs/WelcomeWindow` before the main window; it cannot be closed without saving. Testing a first run means emptying the registry key: back it up first.
- **Registry preferences:** `HKCU\Software\Meu Editor de Vídeo` (`Name`, `Language`, `ThemeColor`, `CheckForNewVersions`, `LastCheck`) via `IUserPreferencesStore`/`RegistryPreferencesStore`; they override `settings.json`. Testing the real app also touches this key; back it up too.
- Controls need `AutomationProperties.Name` (accessibility; also used by UI Automation testing).
- The user manual `documentation/MANUAL_DO_USUARIO.md` ships with the app (Ajuda › Manual, F1). Update it when user-visible behavior changes.

## Working on this machine

- Testing the real app touches the user's data: `%APPDATA%\MeuEditorDeVideo\settings.json` (recent projects) and `%LOCALAPPDATA%\MeuEditorDeVideo\` (autosave, cache, installed AI models, render queue).
  - Back up `settings.json` before driving the app and restore it afterwards.
  - Remove only test-created autosave and project files.
- While the app is running, its `bin\` DLLs are locked and `dotnet build`/`dotnet test` fail with MSB3027. Ask before closing a user's instance; to only check compilation, build into a scratch folder with `-o` (but run the full test suite only with the normal output, since `ExportTemplateStoreTests` fail when outputs are mixed).
- Drive the app through UI Automation (`AutomationProperties.Name`) or Win32 `SendMessage`; don't use `SendKeys` (keys may go to another window).
- When capturing screenshots of the app, capture only its window (e.g. `PrintWindow`), never the full screen.
