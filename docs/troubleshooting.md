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

The current public multilayer APK consumes the staged runtime-hotload CSV from
the companion bundle. If a bundled profile does not show up:

- relaunch the app after pushing the profile so startup diagnostics re-read the
  staged file
- confirm the selected profile is one of the bundled projected-feed Colorama
  profiles
- confirm the app on Quest is the bundled public APK, not an older install

The future `quest_hotload_config` / `quest_twin_state` transport remains a
separate live bridge lane. Do not treat that lane as interchangeable with the
staged CSV hotload path unless you have explicitly verified it in the installed
APK.
