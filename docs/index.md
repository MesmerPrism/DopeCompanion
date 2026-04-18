---
title: Docs Home
description: Start here if you need the Windows operator app that installs, launches, and tunes the public DOPE Quest APKs from Windows.
summary: Public entry point for the DOPE Companion install path, bundled projected-feed Colorama APK, and runtime-config workflow.
nav_label: Docs Home
nav_group: Start Here
nav_order: 10
---

# DOPE Companion

DOPE Companion is the public Windows-side operator surface for the Meta Quest
APKs mirrored out of
`Dynamic Oscillatory Pattern Entrainment`.

The current public focus is the projected-feed multi-layer Colorama scene from
`Assets/Scenes/SynedelicaPassthroughOverlayMultiLayer.unity`. The app is meant
to give operators one place to:

- connect a Quest over USB or Wi-Fi ADB
- install the approved projected-feed Colorama APK
- apply a pinned Quest device profile
- launch the runtime from Windows
- stage scene-specific runtime config from the `Projected Feed Colorama` editor

The public delivery posture follows the same pattern already proven in
`DopeCompanion`: GitHub Pages for docs/install flow and GitHub Releases
for the packaged app assets.

## Current Public Focus

- the bundled Quest payload is the projected-feed Colorama quad build
- the public runtime-config surface is centered on the multi-layer Colorama
  parameters
- the Unity-side `quest_twin_*` bridge is intended to follow the
  `AstralKarateDojo` contract, but the operator repo stays separate from the
  Unity authoring repo
- this package intentionally does not split into a broad “general shell” and a
  second public “study shell” line; the public operator surface is the DOPE
  operator app itself

## Start Here

1. [Download](download.md) the packaged Windows app.
2. [First Session](first-session.md) to connect Quest, install the projected-feed
   Colorama APK, and launch it.
3. [Runtime Config](runtime-config.md) to tune the public projected-feed
   Colorama surface from Windows.

## Choose Your Path

<div class="card-grid">
  <a class="path-card" href="download.md">
    <h3>Install The App</h3>
    <p>Use the packaged app with the same Pages + Releases + certificate-backed install posture as the sibling public operator repo.</p>
  </a>
  <a class="path-card" href="first-session.md">
    <h3>First Operator Pass</h3>
    <p>Connect the headset, install the mirrored projected-feed Colorama APK, apply a device profile, and launch it from Windows.</p>
  </a>
  <a class="path-card" href="runtime-config.md">
    <h3>Projected Feed Colorama</h3>
    <p>Use the dedicated runtime-config section for the projected-feed gradient, displacement, depth-visualization, and overlay-quad controls.</p>
  </a>
  <a class="path-card" href="private-split.md">
    <h3>Public / Private Split</h3>
    <p>See what lives in this public operator repo versus the separate DOPE Unity source repo.</p>
  </a>
  <a class="path-card" href="getting-started.md">
    <h3>Build From Source</h3>
    <p>Use the source build only when you are changing the operator app itself.</p>
  </a>
</div>

## Read These First

- [Download](download.md)
- [First Session](first-session.md)
- [Runtime Config](runtime-config.md)
- [Monitoring and Control](monitoring-and-control.md)
- [Public / Private Split](private-split.md)

