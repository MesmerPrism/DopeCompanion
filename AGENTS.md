# DOPE Companion Agent Guide

If the task expands into machine-wide or cross-project context, use
`$bureau-context` first.

This repo is the public Windows/operator companion for
`C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment`.

## Build And Validation

- Build: `dotnet build DopeCompanion.sln`
- Test: `dotnet test DopeCompanion.sln`
- Run app: `powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1`
- Run CLI: `dotnet run --project src/DopeCompanion.Cli`
- Build docs site: `npm run pages:build`
- Build MSIX package: `powershell -ExecutionPolicy Bypass -File .\tools\app\Build-App-Package.ps1 -Unsigned`

## Public / Private Split

This is the public repo. It ships:

- Windows operator shell
- CLI
- packaging and GitHub Pages delivery
- bundled approved APK mirrors
- public runtime-config profiles and docs

It does not ship:

- the DOPE Unity source repo
- scene-authoring code
- unpublished study configs
- build-time scene wiring changes

If the Quest runtime needs changes, make them in
`C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment` first and
then refresh the mirrored APK here.

## Source Repo Contract

- The authoritative Unity source repo is
  `C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment`.
- The current bundled public target is the projected-feed multi-layer Colorama
  build from
  `Builds\Quest\DynamicOscillatoryPatternEntrainment-SynedelicaPassthroughOverlayMultiLayer.apk`.
- Use `tools\app\Sync-Bundled-Dope-Apk.ps1` to refresh the bundled APK mirror
  and pinned hash metadata.
- Do not add build-time scene mutation logic to this repo.

## Twin / Hotload Posture

- The intended desktop ↔ Quest contract follows the `AstralKarateDojo`
  `quest_twin_state`, `quest_twin_commands`, and `quest_hotload_config`
  transport pattern.
- The bundled scene-profile CSV path is already live for the public projected-
  feed Colorama APK.
- The companion-side twin transport may still be scaffolded here before every
  live `quest_twin_*` lane in the Unity runtime is fully wired.
- Do not claim that live twin publish/monitoring is complete unless the DOPE
  runtime is actually publishing the matching `quest_twin_*` streams.

## Runtime Config Focus

- The primary public tuning surface is the projected-feed multi-layer Colorama
  scene.
- Keep scene-facing keys explicit and self-describing.
- Prefer stable snake_case hotload keys in the public profiles and docs.
- Keep the curated operator-facing keys small; use `Additional Keys` only for
  overflow or experimental fields.

## Release / Certificate Rules

- Follow the same Pages + Releases + preview-certificate posture documented for
  `DopeCompanion`.
- Ship the release asset set together:
  - preview setup EXE
  - `.msix`
  - `.appinstaller`
  - `.cer`
  - portable zip(s)
  - `SHA256SUMS.txt`
- Sign both the MSIX and the helper EXE.
- Validate extracted packaged payload signatures, not just the outer MSIX.
- Keep a manual `.cer` + `.appinstaller` path documented even when the helper
  EXE is the preferred install route.

## Editing Guardrails

- Keep new operator scripts under `tools/`.
- Keep new user-facing docs under `docs/`.
- Prefer updating the bundled `quest-session-kit` metadata over inventing new
  one-off catalog paths.
- Avoid copying Unity source or generated scene content into this repo.

