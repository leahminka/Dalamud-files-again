using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace InventoryDiscardAssistant;

public sealed record InventoryCandidate(uint ItemId, string Name, uint Quantity, GameInventoryType Container, uint Slot, bool IsHq);

public sealed class InventoryScanner(IGameInventory inventory, IDataManager dataManager)
{
    private static readonly GameInventoryType[] InventoryContainers =
    [
        GameInventoryType.Inventory1, GameInventoryType.Inventory2,
        GameInventoryType.Inventory3, GameInventoryType.Inventory4
    ];

    public IReadOnlyList<InventoryCandidate> Scan(bool includeInventory, bool includeArmory)
    {
        var result = new List<InventoryCandidate>();
        var containers = new List<GameInventoryType>();
        if (includeInventory) containers.AddRange(InventoryContainers);

        // Armory is intentionally not included until its exact container enum set is
        // verified for the target client build. This avoids scanning unrelated storage.
        foreach (var container in containers)
        {
            foreach (var slot in inventory.GetInventoryItems(container))
            {
                if (slot.IsEmpty || slot.ItemId == 0) continue;
                var row = dataManager.GetExcelSheet<Item>()?.GetRow(slot.BaseItemId);
                if (row == null) continue;
                result.Add(new InventoryCandidate(slot.ItemId, row.Value.Name.ToString(),
                    slot.Quantity, container, slot.InventorySlot, slot.IsHq));
            }
        }
        return result;
    }
}
