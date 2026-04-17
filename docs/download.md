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

The public release page is:

- [MesmerPrism/DopeCompanion Releases](https://github.com/MesmerPrism/DopeCompanion/releases)

If a direct asset link returns `404`, open the Releases page first. That means
the latest packaged preview has not been published yet.

<div class="download-start">
  <section class="download-path download-path-primary">
    <h2>Guided Install</h2>
    <p>Use the helper EXE when the latest packaged preview release is available.</p>
    <div class="action-row">
      <a class="button primary" href="https://github.com/MesmerPrism/DopeCompanion/releases/latest/download/DopeCompanion-Preview-Setup.exe">Download Guided Install Helper</a>
      <a class="button" href="https://github.com/MesmerPrism/DopeCompanion/releases">Open Releases</a>
    </div>
    <ol class="step-list">
      <li>Download <code>DopeCompanion-Preview-Setup.exe</code>.</li>
      <li>Let the helper trust the preview certificate and install or update the packaged app.</li>
      <li>Launch <code>DOPE Companion Preview</code> from the Start menu if Windows does not open it automatically.</li>
      <li>Continue with <a href="first-session.html">First Session</a>.</li>
    </ol>
  </section>

  <section class="download-path">
    <h2>Manual Install</h2>
    <p>Use this path when Smart App Control or certificate policy blocks the helper.</p>
    <div class="action-row">
      <a class="button" href="https://github.com/MesmerPrism/DopeCompanion/releases/latest/download/DopeCompanion.cer">Download Certificate</a>
      <a class="button" href="https://github.com/MesmerPrism/DopeCompanion/releases/latest/download/DopeCompanion.appinstaller">Download App Installer</a>
      <a class="button" href="https://github.com/MesmerPrism/DopeCompanion/releases/latest/download/DopeCompanion.msix">Download MSIX</a>
    </div>
    <ol class="step-list">
      <li>Download <code>DopeCompanion.cer</code>.</li>
      <li>Install it into <code>Local Machine &gt; Trusted People</code>.</li>
      <li>Download <code>DopeCompanion.appinstaller</code>.</li>
      <li>Open the downloaded <code>.appinstaller</code> file from disk.</li>
      <li>If that still fails, install the direct <code>DopeCompanion.msix</code> package.</li>
    </ol>
  </section>
</div>

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
