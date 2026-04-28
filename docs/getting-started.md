---
title: Build From Source
description: Local source-build path for changing the operator app itself.
summary: Use this only when you are editing the companion repo. Operators should prefer the packaged Windows app.
nav_label: Build From Source
nav_group: Developer
nav_order: 70
---

# Build From Source

Use this path only if you are editing the companion repo itself.

```powershell
git clone <repo-url> DopeCompanion
cd DopeCompanion
dotnet build DopeCompanion.sln
dotnet test DopeCompanion.sln
```

The bundled public APK mirrors are stored directly in Git. A normal clone is
enough for source builds and local Pages builds; Git LFS is not part of the
current public release path.

Run the app with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Build the docs site with:

```powershell
npm install
npm run pages:build
```
