# Safety and implementation notes

## Execution contract

- There is no master enable switch and no unattended auto-discard mode.
- The user starts every operation through the UI: scan, select, build the discard preview, and explicitly click the final confirmation button.
- Holding Ctrl while clicking the final button is required by default. The user may disable this extra input requirement in the settings.
- The game's own native Yes/No prompt is left intact. The plugin never clicks Yes or accepts it.
- The next stack is requested only after the current stack disappears from the original slot.
- If a native prompt is cancelled, the queue waits; the user should press Stop Batch.

## Protection

- Inventory and selected Armoury Chest containers are scanned. EquippedItems is not a scan source.
- Saved gearset equipment IDs are read from RaptureGearsetModule and excluded.
- The Protect list is an explicit user-maintained exclusion list.
- Exclusions override all inclusion filters.
- If gearset data is unavailable, the operation is stopped.
- Before each request, the code revalidates the exact container, slot, item ID, and quantity.

## Interop boundary

Dalamud's documented IGameInventory service is used for inventory snapshots. The discard operation is not exposed as a managed IGameInventory method, so the implementation invokes the game's native AgentInventoryContext entry point via FFXIVClientStructs. This is unsafe client interop and may need updating after game/Dalamud changes. The native confirmation remains under the user's control.

## Verification

A successful API 15 build and in-game test are required before treating this as fully tested. Test with a disposable item, verify both Ctrl-setting modes, test exclusion precedence, verify a saved gearset item is never listed, and confirm that cancelling the native prompt leaves the item untouched.
