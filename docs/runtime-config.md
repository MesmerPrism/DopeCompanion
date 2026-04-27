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
- `Rusty DOPE Feedback Border Baseline`
- `Rusty DOPE Feedback Border Soft`

The bundled profile CSVs are stored in:

- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-baseline.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-soft-gradient.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-balanced-gradient.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-quality.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-strong-gradient.csv`
- `samples/quest-session-kit/HotloadProfiles/rusty-dope-colorama-feedback-border-baseline.csv`
- `samples/quest-session-kit/HotloadProfiles/rusty-dope-colorama-feedback-border-soft.csv`

## Important Boundary

The current public repo ships a working bundled profile path for the Unity
multilayer Colorama APK: the Quest runtime consumes the staged CSV from
`runtime_hotload/runtime_overrides.csv` and reports the applied profile in
startup diagnostics.

The future `quest_twin_*` / `quest_hotload_config` LSL lane is still a
separate transport contract. Treat bundled profile staging as the verified
public baseline and LSL twin-state mirroring as an additional bridge lane.

The Rusty DOPE Colorama feedback-border APK is listed in the same Quest
library. It uses Rust-specific profiles scoped to `com.tillh.rustydopexr`;
do not reuse the Unity profiles because Rusty DOPE has different full-lens
overscan and feedback-border defaults. The Companion upload path is the same
staged CSV file contract. For Rusty DOPE, the CLI stages through `run-as` into
`/data/user/0/com.tillh.rustydopexr/files/runtime_hotload/runtime_overrides.csv`
so the Rust app can read the file from its own sandbox; live variable setters,
twin readback, and focused/media streaming remain future work for the Rust
target.
