# Sample Session Kit

This folder is the committed public Quest Session Kit mirror used by the DOPE
Windows companion.

It carries three operator-facing contracts:

- `APKs/library.json`
- `HotloadProfiles/profiles.json`
- `DeviceProfiles/profiles.json`

The public line is intentionally narrow:

- one bundled Unity DOPE projected-feed Colorama APK
- one bundled Rust/Makepad Colorama feedback-border APK
- three bundled Quest device profiles
- five bundled scene profiles focused on the projected-feed multi-layer Colorama
  surface

The packaged Windows install uses this local mirror first so the public repo
stays self-contained and does not depend on a private Unity checkout.

The Unity APK and scene profiles are expected to work together: the current
public Quest build consumes the `runtime_hotload/runtime_overrides.csv` payload
from this kit and reports the active profile in startup diagnostics.

The Rust APK is a separate OpenXR/Makepad target. The companion can install and
launch it, and launch applies the Rusty-DOPE permission/property bootstrap
(`android.permission.CAMERA`, `horizonos.permission.HEADSET_CAMERA`, scene
permission attempts, media projection app-op, and the tested
`debug.rustydope.*` render/capture values).
Do not expect the Unity CSV hotload profiles, runtime-config variable setters,
quest_twin readback, or focused/media streaming controls to affect this target
yet.

The bundled APK lives in Git LFS. After cloning for development or local
packaging, run `git lfs pull` before expecting the real bytes to exist under
`APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk` or
`APKs/RustyDOPE-ColoramaFeedbackBorder.apk`.
