---
title: Download & Install
description: Install the DOPE Companion Windows preview from the latest public release, or fall back to a source build if you are changing the operator app itself.
summary: Use the same guided helper EXE and manual certificate plus App Installer fallback used by the sibling public operator repo.
nav_label: Download
nav_group: Start Here
nav_order: 15
layout: focused
---

# Download & Install

The intended public install path is the packaged Windows preview, not a repo
checkout.

The release asset set follows the same public preview rules used by the sibling
operator repo:

- guided setup helper EXE
- `.msix`
- `.appinstaller`
- preview signing `.cer`
- portable zip
- `SHA256SUMS.txt`

Start with the guided helper on permissive Windows machines. On machines with
Smart App Control or other download-reputation policy, use the manual
certificate + `.appinstaller` flow first.

The public download URLs are expected to live at:

- `https://github.com/Zivilkannibale/DopeCompanion/releases/latest/download/DopeCompanion-Preview-Setup.exe`
- `https://github.com/Zivilkannibale/DopeCompanion/releases/latest/download/DopeCompanion.appinstaller`
- `https://github.com/Zivilkannibale/DopeCompanion/releases/latest/download/DopeCompanion.msix`
- `https://github.com/Zivilkannibale/DopeCompanion/releases/latest/download/DopeCompanion.cer`

## Guided Install

1. Download `DopeCompanion-Preview-Setup.exe`.
2. Let the helper trust the preview certificate and install or update the
   packaged app.
3. Launch `DOPE Companion Preview` from the Start menu if Windows does not open
   it automatically.
4. Continue with [First Session](first-session.md).

## Manual Install

1. Download `DopeCompanion.cer`.
2. Install it into `Local Machine > Trusted People`.
3. Download `DopeCompanion.appinstaller`.
4. Open the downloaded `.appinstaller` file from disk.
5. If that still fails, install the direct `DopeCompanion.msix` package.

## Before You Start

- Windows 10 or later
- a Quest headset with developer mode enabled
- one USB cable for the first ADB trust step
- local admin approval for the preview certificate trust step

## If You Need A Repo Build

Use the source build only if you are changing the companion app itself:

```powershell
dotnet build DopeCompanion.sln
dotnet run --project src/DopeCompanion.App
```

If Windows policy blocks the unpackaged WPF build, use:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```
