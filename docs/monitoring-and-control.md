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

The app keeps the same future-facing twin transport contract as the sibling
operator repo:

- `quest_twin_commands`
- `quest_twin_state`
- `quest_hotload_config`

Treat that as the intended contract, not as proof that the current DOPE build
is already publishing every lane. The public companion can be ready before the
Unity-side DOPE bridge is fully wired.
