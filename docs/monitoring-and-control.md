---
title: Monitoring & Control
description: Windows operator controls for the public DOPE Quest runtime mirror.
summary: The current public shell covers Quest connection, APK install/launch, device profiles, and staged runtime-config editing.
nav_label: Monitoring & Control
nav_group: Operator Guides
nav_order: 40
---

# Monitoring & Control

The public companion is the Windows-side operator surface. The participant-facing
runtime stays on Quest.

The current public control lanes are:

- Quest connection over USB or Wi-Fi ADB
- bundled APK install
- Quest device-profile application
- app launch
- staged runtime-config editing for the projected-feed Colorama scene
- staged hotload profile upload for the projected-feed Colorama scene
- live-session editing in a focused popout window
- optional `Display 0` Quest cast through a detected local `scrcpy` runtime

The app keeps the same future-facing twin transport contract as the sibling
operator repo:

- `quest_twin_commands`
- `quest_twin_state`
- `quest_hotload_config`

Treat that as the intended contract, not as proof that the current DOPE build
is already publishing every lane. The current public baseline is: bundled
install/launch plus staged hotload profile upload into the live APK, with
startup diagnostics readback after launch.

## Cast Runtime Note

The public packaged app now includes the live cast controls, including the
resize-aware `Display 0` reload path. The cast runtime itself still comes from
an existing local `scrcpy` install rather than from the DOPE release assets.

- official upstream project: [Genymobile/scrcpy](https://github.com/Genymobile/scrcpy)
- upstream license: Apache License 2.0
- DOPE public dependency boundary:
  [THIRD_PARTY_DEPENDENCIES.md](../THIRD_PARTY_DEPENDENCIES.md)
