---
title: CLI Reference
description: Command-line entry points for the public DOPE companion shell.
summary: Use the CLI when you want the same Quest install, launch, status, and twin probes without opening the WPF desktop app.
nav_label: CLI Reference
nav_group: Developer Path
nav_order: 80
---

# CLI Reference

The DOPE companion CLI command is `dope-companion`.

## Current Public Scope

For this repo, the CLI is useful for:

- Quest USB and Wi-Fi ADB setup
- headset status and foreground-app checks
- APK install and launch
- bundled hotload profile upload
- CPU and GPU hint changes
- catalog inspection
- simple LSL monitor and twin-command probes
- managed Quest tooling installation and status checks

The public DOPE line does **not** currently ship a separate locked study-shell
workflow. The inherited `study` and Dope-specific command families may still
exist in the shared scaffold, but they are not the intended public workflow for
this repo and the bundled study-shell catalog is intentionally empty.

## Running

From a source checkout:

```powershell
dotnet run --project src/DopeCompanion.Cli -- <command> [options]
```

Or install as a local/global tool:

```powershell
dotnet pack src/DopeCompanion.Cli
dotnet tool install --global --add-source src/DopeCompanion.Cli/bin/Release dope-companion
dope-companion --help
```

## Core Commands

### Device Connection

| Command | Description |
|---------|-------------|
| `probe` | Detect Quest devices connected via USB |
| `wifi` | Enable Wi-Fi ADB on the USB-connected Quest |
| `connect <endpoint>` | Connect to a Quest over Wi-Fi |
| `status` | Query headset connection, battery, and foreground app |

### App Management

| Command | Description |
|---------|-------------|
| `install <apk>` | Install an APK on the connected Quest |
| `launch <package>` | Launch an app by package ID |
| `perf <cpu> <gpu>` | Set Quest CPU and GPU performance levels |

### Catalog

| Command | Description |
|---------|-------------|
| `catalog list` | List bundled apps, device profiles, and hotload profiles |

### Hotload Profiles

| Command | Description |
|---------|-------------|
| `hotload list` | List bundled projected-feed Colorama hotload profiles |
| `hotload push <profile-id>` | Copy a bundled hotload CSV into the Quest app's `runtime_hotload` folder |

### LSL + Twin

| Command | Description |
|---------|-------------|
| `monitor` | Monitor an LSL stream continuously |
| `twin status` | Show twin bridge status and requested vs reported settings |
| `twin send <action>` | Send a one-shot twin command such as `twin-start` |

Defaults:

- monitor stream name: `quest_monitor`
- monitor stream type: `quest.telemetry`
- channel: `0`

### Utilities

| Command | Description |
|---------|-------------|
| `utility home` | Return to the Quest launcher |
| `utility back` | Send a back event |
| `utility list` | List installed packages |
| `utility reboot` | Reboot the Quest |

### Tooling

| Command | Description |
|---------|-------------|
| `tooling status` | Show the local managed Quest tooling cache state |
| `tooling install-official` | Install or update managed `hzdb`, Android platform-tools, and `scrcpy` |
| `windows-env analyze` | Check Windows-side `adb`, `hzdb`, liblsl, and common network hazards |

## Current DOPE Workflow

For the public projected-feed Colorama line, the practical CLI path is:

```powershell
dope-companion probe
dope-companion wifi
dope-companion connect <quest-ip>:5555
dope-companion status
dope-companion catalog list
dope-companion install samples/quest-session-kit/APKs/DynamicOscillatoryPatternEntrainment-ProjectedFeedColoramaQuad.apk
dope-companion launch com.tillh.dynamicoscillatorypatternentrainment
dope-companion hotload list
dope-companion hotload push dope_projected_feed_colorama_balanced_gradient
```

If you want to inspect the separate LSL twin lane after the Unity runtime gains
that bridge:

```powershell
dope-companion twin status
dope-companion twin send twin-start
dope-companion monitor --stream quest_monitor --type quest.telemetry
```

## Environment Variables

The important environment variables for this repo are:

| Variable | Purpose |
|----------|---------|
| `DOPE_QUEST_SESSION_KIT_ROOT` | Override the bundled Quest Session Kit root |
| `DOPE_OPERATOR_DATA_ROOT` | Override the host-visible operator data root |
| `DOPE_ADB_EXE` | Override the `adb.exe` path |
| `DOPE_HZDB_EXE` | Override the `hzdb.exe` path |
| `DOPE_SCRCPY_EXE` | Override the `scrcpy.exe` path |
| `DOPE_LSL_DLL` | Override the `lsl.dll` path |

## Notes

- `windows-env analyze` is the best first machine-level check when LSL or Wi-Fi
  ADB behavior looks wrong.
- The bundled public DOPE APK is currently the install and launch source of
  truth.
- The bundled projected-feed runtime consumes the staged hotload CSVs from this
  repo today.
- The future `quest_twin_*` and `quest_hotload_config` LSL lane is separate
  from that staged file-upload path.

