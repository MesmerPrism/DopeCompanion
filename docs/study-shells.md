---
title: Operator Surface
description: Packaging posture for the public DOPE operator app.
summary: This public line intentionally does not split into a separate general shell and study-specific package; the DOPE operator app is the public surface.
nav_label: Operator Surface
nav_group: Operator Guides
nav_order: 35
---

# Operator Surface

Unlike the sibling public operator repo, this public line is not being set up
as two separate public packages right now.

The intent is:

- one public DOPE operator app
- one bundled projected-feed Colorama Quest APK mirror
- one Windows runtime-config surface centered on the multi-layer Colorama scene

That means the current public line does **not** rely on a separate public
general-shell package plus a second study-locked package. The public operator
surface is the DOPE operator app itself.

If later workflow constraints justify a more locked-down study shell, it can be
added as data/config on top of the same operator codebase. That is not the
current public posture.
