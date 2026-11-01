# Safety and implementation notes

## Execution contract

- Master enable is off by default.
- No background or automatic scan/discard loop exists.
- The user starts a batch with a UI button.
- Preview is on by default; when enabled, the user reviews the stack list and confirms the batch.
- With preview off, the user can start the batch directly after scanning.
- The game's own native Yes/No prompt is left intact. The plugin never clicks Yes or accepts it.
- The next stack is requested only after the current stack disappears from the original slot.
- If a native prompt is cancelled, the queue waits; the user should press Stop Batch.

## Protection

- Inventory and selected Armoury Chest containers are scanned. EquippedItems is not a scan source.
- Saved gearset equipment IDs are read from RaptureGearsetModule and excluded.
- The Protect/Favorite list is an explicit user-maintained exclusion list.
- Exclusions override all inclusion filters.
- If gearset data is unavailable, scanning is aborted.
- Before each request, the code revalidates the exact container, slot, item ID, and quantity.

## Interop boundary

Dalamud's documented IGameInventory service is used for inventory snapshots. The discard operation is not exposed as a managed IGameInventory method, so the implementation invokes the game's native AgentInventoryContext entry point via FFXIVClientStructs. This is unsafe client interop and may need updating after game/Dalamud changes. The native confirmation remains under the user's control.

## Verification still required

A successful API 15 build and in-game test are required before treating this as release-ready. Test with a disposable item, verify both preview modes, test exclusion precedence, verify a saved gearset item is never listed, and confirm that cancelling the native prompt leaves the item untouched.
