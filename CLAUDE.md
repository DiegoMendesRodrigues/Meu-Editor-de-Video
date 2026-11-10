# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Meu Editor de Vídeo** — non-linear video editor for Windows 11 x64. C#/.NET 10, WPF with the Fluent theme (`ThemeMode`), MVVM via CommunityToolkit.Mvvm, DI via Microsoft.Extensions.Hosting. FFmpeg (via FFmpeg.AutoGen), ONNX Runtime (DirectML/CPU), Whisper.net, NAudio (WASAPI), OpenCvSharp. License GPL-3.0-or-later.

- UI text, docs (`documentation/`), code comments, and commit messages are in **Portuguese (pt-BR)**.
- Spec: `ESPECIFICACAO_TECNICA_E_FUNCIONAL.md` (RF/RNF/CA ids referenced in code and comments).
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
pwsh scripts/generate-notices.ps1                 # regenerate THIRD-PARTY-NOTICES.txt after any dependency change
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
- `ModelDownloads`: the **only** assembly allowed to use the network (HTTP to pinned URLs with SHA-256, only when the user clicks Instalar). `PrivacyTests` fails the build if any other product assembly touches `System.Net.*`. Keep it that way. A new product assembly must be added to `ProductAssemblies` in those tests.

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

### App (WPF) conventions

- **Navigation:** pages are `PageViewModel`s mapped to views in `Views/ViewMappings.xaml`. `INavigationService.NavigateTo<T>()` switches pages; `OnNavigatedTo()` refreshes state.
- **Dialogs:** go through `IDialogService` (`Services/DialogService.cs`), so view models stay testable with fakes.
  - Adding a method means updating the fakes in `tests/MeuEditorDeVideo.UnitTests/Application/*Tests.cs`.
  - Simple custom dialogs are code-only `Window`s in `Views/Dialogs/` (e.g. `PromptWindow`, `RecentRemovalWindow`).
- **Strings:** `Resources/Strings/{Common,Home,Editor}.xaml` (`sys:String` keys like `Home.ProjetosRecentes`). Theme brushes are `DynamicResource` keys `Med.*` (`Med.Text`, `Med.Background`, `Med.Panel`, `Med.Accent`, `Med.Danger`…), defined in `Themes/Palette.{Dark,Light}.xaml` and swapped by `ThemeService`.
- **Fluent styling pitfall:** a `Style` without `BasedOn="{StaticResource {x:Type X}}"` replaces the Fluent template with the classic Aero one. On dark windows this produces white-on-white dropdowns and lists. Never apply generic styles (e.g. `TargetType="FrameworkElement"` just for margins) to controls; set `Margin` directly. `ItemContainerStyle`s must be `BasedOn` the theme style.
- Controls need `AutomationProperties.Name` (accessibility; also used by UI Automation testing).
- The user manual `documentation/MANUAL_DO_USUARIO.md` ships with the app (Ajuda › Manual, F1). Update it when user-visible behavior changes.

## Working on this machine

- Testing the real app touches the user's data: `%APPDATA%\MeuEditorDeVideo\settings.json` (recent projects) and `%LOCALAPPDATA%\MeuEditorDeVideo\` (autosave, cache, installed AI models, render queue).
  - Back up `settings.json` before driving the app and restore it afterwards.
  - Remove only test-created autosave and project files.
- When capturing screenshots of the app, capture only its window (e.g. `PrintWindow`), never the full screen.
