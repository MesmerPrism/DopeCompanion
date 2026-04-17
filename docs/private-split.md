---
title: Public / Private Split
description: Boundary between the public operator repo and the separate DOPE Unity source repo.
summary: This public repo mirrors approved APK payloads and operator tooling only; the Unity source repo remains separate.
nav_label: Public / Private Split
nav_group: Developer
nav_order: 60
---

# Public / Private Split

This public repo exists so the Windows operator surface and approved APK
payloads can be shared without exposing the Unity source project.

## Public

- Windows app
- CLI
- packaging and installer flow
- Pages docs
- bundled approved Quest APK mirrors
- public runtime-config profiles and schemas

## Private / Source Side

- the Unity source repo:
  `C:\Users\tillh\source\repos\Dynamic Oscillatory Pattern Entrainment`
- scene-authoring code
- source assets and scenes
- build-time scene wiring changes

## Working Rule

If the Quest runtime changes, update the Unity repo first and then refresh the
mirrored APK here with `tools/app/Sync-Bundled-Dope-Apk.ps1`.
