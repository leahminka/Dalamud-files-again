# Inventory Discard Assistant

A user-controlled inventory cleanup plugin for Final Fantasy XIV / Dalamud API 15.

## Features

- Open or close the plugin window with `/aid`; the title-bar X also closes it.
- The plugin window does not open automatically when the plugin is enabled.
- Search scanned storage and select individual item types for review.
- Protect item types with a persistent exclusion list.
- Review every selected stack, including its icon, name, HQ marker, quantity, container, and slot.
- Include or exclude individual stacks in the discard preview.
- Resize the inventory and selection panels by dragging the divider.
- Optional Armoury Chest and Chocobo Saddlebag scanning (including Premium Saddlebag when available).
- Quick bulk selection for Fish, Monster Materials, Crafting Materials, Dyes, and Miscellaneous. Monster-material grouping uses item-name clues, so review the preview rather than treating the category as a perfect game taxonomy.
- Glamour Dresser is shown as unsupported; its contents are not scanned or offered for discard.
- Hold Ctrl while clicking the final confirmation button by default. This requirement can be disabled with the **Disable holding Ctrl to discard** setting.
- Every discard request still uses the game's native confirmation dialog.

## Safety behavior

- No background or automatic scan/discard loop exists. A user must scan, select items, review the preview, and explicitly click the final confirmation button.
- Ctrl confirmation is enabled by default. If disabled, the user can click the final button normally.
- The plugin never accepts the game's native Yes/No prompt.
- Protected item IDs override inclusion.
- Saved gearset equipment IDs are excluded. Equipped slots are not scanned.
- Before each discard request, the exact container, slot, item ID, and quantity are revalidated.
- If gearset data cannot be read, the operation is stopped rather than assuming no gearset items exist.

The discard request uses the game's native inventory-context discard entry point through FFXIVClientStructs. This is an unsafe interop boundary and must be rechecked after game/Dalamud updates. It is not a packet injector and does not automate the native confirmation.

## Build

Requirements:
- .NET 10 SDK
- Dalamud API 15 development files installed by XIVLauncher, or set DALAMUD_HOME to a folder containing the Dalamud API assemblies.

On Windows with XIVLauncher installed:

    dotnet build InventoryDiscardAssistant/InventoryDiscardAssistant.csproj --configuration Release

GitHub Actions downloads the current API 15 staging build into the standard Dalamud dev path before compiling.

## Custom repository

Custom Dalamud repository feed: https://raw.githubusercontent.com/leahminka/Dalamud-files-again/main/repo.json

Latest release: https://github.com/leahminka/Dalamud-files-again/releases/tag/v0.1.3

To install: open /xlsettings → Experimental → Custom Plugin Repositories, paste the feed URL above, enable it, and save. Then open /xlplugins and search for Inventory Discard Assistant.
