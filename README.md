# Inventory Discard Assistant

A user-controlled inventory cleanup plugin for Final Fantasy XIV / Dalamud API 15.

## Features in this source version

- Dynamic item-category checkboxes sourced from the game's Item / ItemUICategory sheets.
- Searchable item catalog with individual Include and Protect/Favorite toggles.
- Persistent item selections, protected IDs, category selections, scan preferences, and preview preference.
- Inventory and optional Armoury Chest scanning.
- Preview of matching stacks with item icon, name, HQ marker, quantity, container, and slot.
- Per-stack include/exclude controls in the preview.
- Gearset items are excluded from scan candidates.
- A master enable switch blocks execution when off.
- Preview-before-discard is enabled by default.
- Each discard request opens the game's own native confirmation. The plugin never accepts that prompt.

## Important safety behavior

- The plugin does not discard in the background. A user must press Scan and then the relevant discard action.
- When preview is enabled, the user must review the list and press Confirm Preview & Start.
- When preview is disabled, Discard Matching Items starts the selected batch directly.
- The game still presents its native Yes/No confirmation for each stack. The user must accept each one.
- If a native confirmation is cancelled, press Stop Batch to end the waiting sequence.
- Protected item IDs always override category and individual-item selection.
- Items in equipped slots are not scanned. Items referenced by saved gearsets are excluded.
- The Protect/Favorite list is user-maintained. FFXIV does not expose a general-purpose favorite item flag through the inventory API.
- If gearset data cannot be read, scanning is aborted rather than assuming no gearset items exist.

The discard request uses the game's native inventory-context discard entry point through FFXIVClientStructs. That is an unsafe interop boundary and must be rechecked after game/Dalamud updates. It is not a packet injector and does not automate the native confirmation.

## Build

Requirements:
- .NET 10 SDK
- Dalamud API 15 development files installed by XIVLauncher, or set DALAMUD_HOME to a folder containing the Dalamud API assemblies.

On Windows with XIVLauncher installed:

    dotnet build InventoryDiscardAssistant/InventoryDiscardAssistant.csproj --configuration Release

GitHub Actions downloads the current API 15 staging build into the standard Dalamud dev path before compiling.

## Install for local testing

1. Build the project.
2. In FFXIV, open Dalamud settings with /xlsettings.
3. Under Experimental → Dev Plugin Locations, add the plugin build output folder or the built DLL.
4. Open /xlplugins, find Installed Dev Plugins, and enable Inventory Discard Assistant.
5. Start with a nonessential, low-value test item and verify the game's native confirmation behavior.

## Custom repository

Custom Dalamud repository feed: https://raw.githubusercontent.com/leahminka/Dalamud-files-again/main/repo.json

First packaged release: https://github.com/leahminka/Dalamud-files-again/releases/tag/v0.1.0

To install: open /xlsettings → Experimental → Custom Plugin Repositories, paste the feed URL above, enable it, and save. Then open /xlplugins and search for Inventory Discard Assistant.
