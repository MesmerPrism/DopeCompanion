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
- two bundled scene profiles focused on the projected-feed multi-layer Colorama
  surface

The packaged Windows install uses this local mirror first so the public repo
stays self-contained and does not depend on a private Unity checkout.

The bundled APK lives in Git LFS. After cloning for development or local
packaging, run `git lfs pull` before expecting the real bytes to exist under
`APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk`.
