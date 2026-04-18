# Third-Party Dependencies

This repository's source code is licensed under the [MIT License](LICENSE).

That MIT license applies to the DOPE Companion source tree itself. It
does not relicense third-party runtimes, device tools, APK payloads, or other
upstream artifacts that may be bundled with a release build or fetched by the
operator tooling.

## Current Public Dependency Boundary

### Meta Horizon Debug Bridge (`hzdb`)

- The repo does not ship Meta's `hzdb` Windows binary in source control or in
  the GitHub release assets.
- The guided installer and the CLI `tooling install-official` command fetch
  the current published Windows package from Meta's npm publication:
  `@meta-quest/hzdb-win32-x64`.
- Upstream license/terms:
  [Meta Platform Technologies SDK License Agreement](https://developers.meta.com/horizon/licenses/)
- This repo does not claim to relicense `hzdb` under MIT.

### Android SDK Platform-Tools (`adb`, `fastboot`, related files)

- The repo does not ship Google's Android SDK Platform-Tools in source control
  or in the GitHub release assets.
- The guided installer and the CLI `tooling install-official` command fetch
  the current published Windows package from Google's Android SDK repository
  metadata and download host.
- Upstream license/terms:
  [Android Software Development Kit License Agreement](https://developer.android.com/studio/releases/platform-tools)
- This repo does not claim to relicense Android SDK Platform-Tools under MIT.

### scrcpy (`scrcpy.exe` and related runtime files)

- The public DOPE release does not ship `scrcpy` in source control or as a
  standalone GitHub release asset.
- The guided installer and the CLI `tooling install-official` command fetch
  the published Windows `scrcpy` bundle from the official upstream GitHub
  release and keep it in the managed LocalAppData tool cache.
- The live `Display 0` cast surface resolves `scrcpy` in this order:
  managed LocalAppData copy, app-local `scrcpy.exe`, app-local
  `scrcpy\scrcpy.exe`, the local `Quest Multi Stream\tools\scrcpy` cache, then
  `PATH`.
- Upstream project: [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy)
- Upstream license/terms:
  [Apache License 2.0](https://github.com/Genymobile/scrcpy/blob/master/LICENSE)
- This repo does not claim to relicense `scrcpy` under MIT.

### Bundled Public Study Payloads

- This public repo may mirror explicitly approved public study payloads needed
  by the Windows operator flow, such as the curated Dope APK mirror under
  `samples/quest-session-kit/APKs/`.
- Those payloads are not implicitly relicensed under MIT by virtue of being
  stored or distributed from this repo.

### Bundled Runtime Libraries

- Some packaged Windows builds include third-party runtime libraries needed for
  the operator path, such as the bundled Windows `lsl.dll` runtime.
- Those upstream libraries remain under their own terms. The repo's MIT license
  does not override or replace those upstream terms.

## Practical Rule

If a file or runtime comes from this repo's own source tree, MIT is the default
license unless a more specific notice says otherwise. If a file or runtime is
mirrored from or fetched from an upstream publisher, treat the upstream
publisher's license and terms as authoritative.

