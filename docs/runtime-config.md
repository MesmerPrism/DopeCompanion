---
title: Runtime Config
description: Public Windows-side tuning surface for the projected-feed multi-layer Colorama scene.
summary: The runtime-config editor now includes a dedicated Projected Feed Colorama section with the key scene controls for the multi-layer quad.
nav_label: Runtime Config
nav_group: Operator Guides
nav_order: 50
---

# Runtime Config

The current public tuning focus is the projected-feed multi-layer Colorama
scene from
`Assets/Scenes/SynedelicaPassthroughOverlayMultiLayer.unity`.

The Windows editor keeps that scene in its own `Projected Feed Colorama`
section instead of burying the keys in a generic catch-all list.

## Current Public Keys

The curated operator-facing section covers:

- projected-feed brightness, contrast, saturation, and stereo blend
- gradient span, blend, phase offset, oscillator amount, and audio speed boost
- horizontal and vertical displacement amplitude
- displacement audio boost, blur kernel, blur radius, blur sigma, blend, and
  gradient influence
- depth-visualization enable and near/far meters
- overlay quad size, display-surface mode, overscan, and edge fade

The baseline values come from the authored scene contract in
`ProjectedFeedColoramaQuadSceneAuthoring`.

## Public Profiles

The repo currently bundles:

- `DOPE Projected Feed Colorama Baseline`
- `DOPE Projected Feed Colorama Soft Blur`
- `DOPE Projected Feed Colorama Balanced Blur`
- `DOPE Projected Feed Colorama Quality`
- `DOPE Projected Feed Colorama Strong Gradient Warp`

The bundled profile CSVs are stored in:

- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-baseline.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-soft-gradient.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-balanced-gradient.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-quality.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-strong-gradient.csv`

## Important Boundary

The current public repo ships a working bundled profile path for the Unity
multilayer Colorama APK: the Quest runtime consumes the staged CSV from
`runtime_hotload/runtime_overrides.csv` and reports the applied profile in
startup diagnostics.

The future `quest_twin_*` / `quest_hotload_config` LSL lane is still a
separate transport contract. Treat bundled profile staging as the verified
public baseline and LSL twin-state mirroring as an additional bridge lane.

The Rusty DOPE Colorama feedback-border APK is listed in the same Quest
library, but it does not consume these Unity CSV profiles yet. For that target,
the companion currently supports install, launch, Rusty-DOPE permission grants,
and `debug.rustydope.*` startup properties only; live variable setters,
runtime-config hotload, twin readback, and focused/media streaming are future
work.
