# Dalamud Files — Inventory Discard Assistant

A Dalamud plugin project for a deliberately manual inventory-discard workflow.

## Safety contract

- Discarding is disabled until **Enable Auto-Discard** is explicitly enabled.
- The user must press the discard button to begin an operation.
- Preview-before-discard is enabled by default.
- Exclusions take precedence over every category and item filter.
- Equipped items and items assigned to gearsets must be protected.
- The plugin must never silently accept the game's native discard confirmation dialog.
- Disabling the master switch prevents all discard execution without deleting configuration.

## Project status

This repository is the project home. The implementation is being developed against Dalamud API 15 / .NET 10. A successful build and in-game verification are required before a release is described as ready.

## Build

Requires the .NET 10 SDK and a Dalamud development environment.

```powershell
dotnet restore
dotnet build -c Release
```

The plugin project uses `Dalamud.NET.Sdk/15.0.0`. Build output is configured for the `dist` directory.

## Installation

A custom Dalamud plugin repository needs a public repository feed with a valid plugin manifest and release artifact. This repository is public, but the repository feed and compiled release should only be added after the plugin has been built and verified.

## AI assistance

This project was developed with AI assistance. Review source and test it on a nonessential inventory before relying on it.
