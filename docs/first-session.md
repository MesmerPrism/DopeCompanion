---
title: First Session
description: First operator pass for the public DOPE projected-feed Colorama build.
summary: Connect Quest, install the bundled projected-feed Colorama APK, apply a Quest profile, and launch the runtime from Windows.
nav_label: First Session
nav_group: Start Here
nav_order: 20
---

# First Session

Use this when you are bringing up the public projected-feed Colorama runtime on
a Quest headset from Windows.

## Current Bundled Target

Primary Unity target:

- app: `Dynamic Oscillatory Pattern Entrainment Projected Feed Colorama`
- package id: `com.tillh.dynamicoscillatorypatternentrainment`
- launch activity:
  `com.tillh.dynamicoscillatorypatternentrainment/com.unity3d.player.UnityPlayerGameActivity`
- bundled APK:
  `samples/quest-session-kit/APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk`

Secondary Rust target:

- app: `Rusty DOPE Colorama Feedback Border`
- package id: `com.tillh.rustydopexr`
- launch activity:
  `com.tillh.rustydopexr/com.tillh.rustydopexr.MakepadApp`
- bundled APK:
  `samples/quest-session-kit/APKs/RustyDOPE-ColoramaFeedbackBorder.apk`

## Operator Path

For the Unity projected-feed Colorama target:

1. Plug the Quest in over USB and approve USB debugging in-headset.
2. Open `DOPE Companion`.
3. Run `Probe USB` or the equivalent device snapshot action.
4. Select `DOPE Projected Feed Colorama APK`.
5. Apply either the `DOPE Projected Feed Balanced` or
   `DOPE Projected Feed Quality` device profile.
6. Install the bundled APK.
7. Launch the app.
8. Open [Runtime Config](runtime-config.md) and select the projected-feed
   baseline, soft-blur, balanced-blur, quality, or strong-gradient profile if
   you want to restage the scene from Windows.

For the Rusty DOPE Colorama target:

1. Plug the Quest in over USB and approve USB debugging in-headset.
2. Open `DOPE Companion`.
3. Run `Probe USB`.
4. Select `Rusty DOPE Colorama Feedback Border APK`.
5. Optionally apply `Rusty DOPE Colorama Balanced`; launch also applies the
   required Rusty-DOPE camera/scene permissions and `debug.rustydope.*`
   startup properties.
6. Install the bundled APK.
7. Launch the app from the companion.
8. For hotload-capable Rusty DOPE builds, open [Runtime Config](runtime-config.md)
   and select `Rusty DOPE Feedback Border Baseline` or
   `Rusty DOPE Feedback Border Soft`.

## Notes

- The current public Windows repo bundles the APK mirror and a working staged
  hotload profile path for the multilayer Colorama runtime.
- The runtime reports the active staged profile in startup diagnostics after
  launch, which is the supported public readback path today.
- The future `quest_twin_state` / `quest_hotload_config` live transport still
  follows the `AstralKarateDojo` contract, but treat that as a separate lane
  from the verified staged profile path.
- The Rust target uses Rust-specific staged CSV profiles. Unity CSV profiles,
  live variable setters, twin readback, and focused/media streaming controls do
  not affect it.
