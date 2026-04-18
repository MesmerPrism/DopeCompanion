# DOPE Companion

DOPE Companion is the public Windows operator app for the Meta Quest APKs
produced by
`C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment`.

This repo is intentionally separate from the Unity source repo. It ships the
Windows-side install, launch, monitoring, runtime-config, and packaging
surface, plus bundled approved APK payloads. It does not ship the Unity
project itself.

The current public focus is the projected-feed multi-layer Colorama scene from
`Assets/Scenes/SynedelicaPassthroughOverlayMultiLayer.unity`. The bundled Quest
runtime mirror is:

- package id: `com.tillh.dynamicoscillatorypatternentrainment`
- launch activity:
  `com.tillh.dynamicoscillatorypatternentrainment/com.unity3d.player.UnityPlayerGameActivity`
- mirrored APK:
  `samples/quest-session-kit/APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk`
- current bundled SHA256:
  `5819A09C66131B0597025DED63D00A3CA7ED4450DCF7ECAF98E460EC4F9B45C6`

The public operator surface follows the same delivery posture proven in
`DopeCompanion`:

- GitHub Pages is the stable install and docs surface.
- GitHub Releases is the binary source of truth.
- the release asset set includes a preview MSIX, `.appinstaller`, public
  `.cer`, guided setup EXE, checksums, and portable zips.
- signing and certificate handling follow the same preview-certificate
  guidelines documented in the Agent Bureau and the sibling repo.

Current status:

- the Windows app scaffold is derived from the generic operator shell in
  `DopeCompanion`
- the bundled session kit now targets the verified hotload-capable DOPE
  projected-feed Colorama APK
- the runtime-config editor exposes a dedicated `Projected Feed Colorama`
  section with the multi-layer quad controls
- the bundled scene-profile CSV path is live in the Quest runtime and reports
  the active hotload profile in startup diagnostics
- the packaged app and bundled CLI now expose a one-shot full diagnostic
  harness that can reinstall the public APK, apply the baseline device and
  scene profiles, relaunch the runtime, and emit a shareable diagnostics bundle
- the live-session window now includes a public `Display 0` cast control that
  resizes cleanly by restarting the managed `scrcpy` session with the requested
  bounds
- the future `quest_twin_*` LSL lane still follows the
  `AstralKarateDojo` transport contract, but that remains separate from the now
  verified bundled CSV hotload path

## Scope

This public repo ships:

- WPF desktop operator shell
- CLI and release packaging scaffold
- Pages docs and install surface
- bundled approved Quest APK mirrors
- public runtime-config profiles for the projected-feed Colorama scene
- sync tooling that refreshes the bundled APK mirror from the Unity source repo

It does not ship:

- the Unity source project
- scene authoring code from the DOPE repo
- unpublished private study presets
- build-time scene mutation logic
- `scrcpy` as a standalone release asset or source-controlled binary

The guided installer and `dope-companion tooling install-official` fetch and
maintain the published Windows `scrcpy` bundle in the managed LocalAppData tool
cache instead.

If the Quest runtime itself needs to change, do that first in
`C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment`, then
refresh the mirrored APK here.

## Start Here

- Docs home: [docs/index.md](docs/index.md)
- Install guide: [docs/download.md](docs/download.md)
- First operator pass: [docs/first-session.md](docs/first-session.md)
- Runtime tuning surface: [docs/runtime-config.md](docs/runtime-config.md)
- Public/private split: [docs/private-split.md](docs/private-split.md)

## Development

```powershell
git clone <repo-url> DopeCompanion
cd DopeCompanion
git lfs install
git lfs pull
dotnet build DopeCompanion.sln
dotnet test DopeCompanion.sln
```

If Windows policy blocks the unpackaged WPF build, use the repo launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Build the Pages site locally with:

```powershell
npm install
npm run pages:build
```

## Bundled APK Refresh

Refresh the bundled projected-feed Colorama APK mirror from the Unity repo
with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Sync-Bundled-Dope-Apk.ps1
```

That copies the approved APK into `samples/quest-session-kit/APKs/` and updates
the pinned compatibility hash used by the public operator app and release
artifacts.

