# Sample Session Kit

This folder is the committed public Quest Session Kit mirror used by the DOPE
Windows companion.

It carries three operator-facing contracts:

- `APKs/library.json`
- `HotloadProfiles/profiles.json`
- `DeviceProfiles/profiles.json`

The public line is intentionally narrow:

- one bundled DOPE projected-feed Colorama APK
- two bundled Quest device profiles
- five bundled scene profiles focused on the projected-feed multi-layer Colorama
  surface

The packaged Windows install uses this local mirror first so the public repo
stays self-contained and does not depend on a private Unity checkout.

The bundled APK and scene profiles are expected to work together: the current
public Quest build consumes the `runtime_hotload/runtime_overrides.csv` payload
from this kit and reports the active profile in startup diagnostics.

The bundled APK lives in Git LFS. After cloning for development or local
packaging, run `git lfs pull` before expecting the real bytes to exist under
`APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk`.
