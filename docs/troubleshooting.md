---
title: Troubleshooting
description: First-pass troubleshooting for the public DOPE operator app.
summary: Focus first on USB/Wi-Fi ADB setup, bundled APK selection, and the current Unity-side twin-bridge boundary.
nav_label: Troubleshooting
nav_group: Start Here
nav_order: 80
---

# Troubleshooting

## The app installed, but Quest connection is still failing

- confirm Quest developer mode is enabled
- re-approve USB debugging in-headset
- probe USB before expecting Wi-Fi ADB to work

## Install or launch does nothing

- confirm the selected target is the bundled projected-feed Colorama APK
- confirm the headset is connected before running install or launch
- verify the package id is
  `com.tillh.dynamicoscillatorypatternentrainment`

## Runtime-config changes do not show up live

The Windows-side config surface is ready first. The matching DOPE Unity runtime
still needs the Astral-style `quest_hotload_config` and `quest_twin_state`
bridge wiring before live config push/readback should be treated as complete.

Until that bridge lands in the DOPE runtime:

- use the public profiles here as the contract and staging surface
- do not over-claim live state mirroring
