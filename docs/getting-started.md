---
title: Build From Source
description: Local source-build path for changing the operator app itself.
summary: Use this only when you are editing the companion repo. Operators should prefer the packaged Windows preview.
nav_label: Build From Source
nav_group: Developer
nav_order: 70
---

# Build From Source

Use this path only if you are editing the companion repo itself.

```powershell
git clone <repo-url> DopeCompanion
cd DopeCompanion
git lfs install
git lfs pull
dotnet build DopeCompanion.sln
dotnet test DopeCompanion.sln
```

Run the app with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Build the docs site with:

```powershell
npm install
npm run pages:build
```
