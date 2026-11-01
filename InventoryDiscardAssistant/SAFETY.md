# Safety and implementation notes

This first source drop intentionally does not issue discard commands.

Inventory reading uses Dalamud's documented IGameInventory service. The item-discard operation itself is not exposed by that service; a native ClientStructs call would be required. ClientStructs is an unsafe interop layer, so the discard path must be verified against the exact game/API build and must retain the game's native confirmation prompt for the user to accept manually. It must not programmatically click through that prompt.

The current UI supports configuration persistence, master-switch state, preview preference, item-name search, and an inventory scan. It does not yet implement:
- execution of discards,
- complete item category classification,
- individual-item selection database UI,
- gearset/favorite protection,
- armory inventory scanning,
- exclusion list editor.

Those features are not represented as complete or safe until implemented and tested.
