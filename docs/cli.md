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
- one-shot public install-and-diagnostics harness runs with shareable bundles

The public DOPE line does **not** currently ship a separate locked study-shell
workflow. The inherited `study` and Dope-specific command families may still
exist in the shared scaffold, but they are not the intended public workflow for
this repo and the bundled study-shell catalog is intentionally empty.

## Running

From a source checkout:

```powershell
dotnet run --project src/DopeCompanion.Cli -- <command> [options]
```

From the guided installer:

```powershell
# Launch the packaged app once so it prepares the local agent workspace,
# then open Windows Environment -> Open Agent Workspace.
.\dope-companion.ps1 --help
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
| `study run-harness <study>` | Reinstall the pinned APK, apply the device profile and baseline scene profile, launch the app, and write a shareable harness bundle |

Useful `windows-env analyze` options:

- `--local-only`: skip saved headset selectors and ADB-backed Quest Wi-Fi
  transport probes, which is useful for consumer machines where you only want
  local Windows/liblsl diagnostics.
- `--check-timeout-seconds <seconds>`: bound the slow probe steps so diagnostics
  return partial findings instead of hanging behind one blocked network or LSL
  check.

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

For the Rusty DOPE feedback-border target, select or launch
`com.tillh.rustydopexr`, then push one of the Rust-scoped profiles:

```powershell
dope-companion hotload push rusty_dope_colorama_feedback_border_baseline
dope-companion hotload push rusty_dope_colorama_feedback_border_soft
```

Those profiles use the same staged CSV upload path but are intentionally
separate from the Unity projected-feed profiles.

When you need one autonomous public acceptance run that exercises the same
install/profile/launch path the guided installer ships:

```powershell
dope-companion study run-harness dope-projected-feed-colorama
```

That command will:

- verify or install the managed Quest tooling cache when needed
- connect to the headset from the current selector, a remembered Wi-Fi endpoint,
  or USB bootstrap
- reinstall the pinned bundled APK
- apply the pinned Quest device profile
- stage the bundled baseline projected-feed Colorama scene profile
- launch the public DOPE runtime
- generate a shareable harness bundle with JSON, LaTeX, PDF, and summary files

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
- `study run-harness dope-projected-feed-colorama` is the fastest way to
  generate one shareable file for remote support after the guided installer
  path is already on the machine.
- The bundled public DOPE APK is currently the install and launch source of
  truth.
- The bundled Unity projected-feed runtime consumes the staged hotload CSVs
  from this repo today. Rusty DOPE uses separate Rust-scoped staged CSV profiles
  when paired with a hotload-capable Rust build.
- The future `quest_twin_*` and `quest_hotload_config` LSL lane is separate
  from that staged file-upload path.

