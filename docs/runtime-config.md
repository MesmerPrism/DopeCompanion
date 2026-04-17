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
- `DOPE Projected Feed Colorama Quality`

Both profiles are stored in:

- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-baseline.csv`
- `samples/quest-session-kit/HotloadProfiles/dope-projected-feed-colorama-quality.csv`

## Important Boundary

The current public repo defines the Windows-side profile shape and the intended
`quest_hotload_config` payload. The matching DOPE Unity runtime still needs the
Astral-style twin/hotload bridge wired in before live push and state readback
should be treated as complete.
