using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace InventoryDiscardAssistant;

public sealed record InventoryCandidate(
    uint ItemId, uint BaseItemId, string Name, string Category, int Quantity,
    GameInventoryType Container, uint Slot, bool IsHq);

public sealed record CatalogItem(uint ItemId, string Name, uint CategoryId, string CategoryName);

public sealed class InventoryScanner(IGameInventory inventory, IDataManager dataManager)
{
    private static readonly GameInventoryType[] InventoryContainers =
    [
        GameInventoryType.Inventory1, GameInventoryType.Inventory2,
        GameInventoryType.Inventory3, GameInventoryType.Inventory4
    ];

    private static readonly GameInventoryType[] ArmoryContainers =
    [
        GameInventoryType.ArmoryMainHand, GameInventoryType.ArmoryOffHand,
        GameInventoryType.ArmoryHead, GameInventoryType.ArmoryBody,
        GameInventoryType.ArmoryHands, GameInventoryType.ArmoryLegs,
        GameInventoryType.ArmoryFeets, GameInventoryType.ArmoryEar,
        GameInventoryType.ArmoryNeck, GameInventoryType.ArmoryWrist,
        GameInventoryType.ArmoryRings, GameInventoryType.ArmorySoulCrystal
    ];

    public IReadOnlyList<CatalogItem> GetCatalog()
    {
        var sheet = dataManager.GetExcelSheet<Item>();
        var rows = new List<CatalogItem>();
        foreach (var item in sheet)
        {
            if (item.RowId == 0 || item.Name.IsEmpty) continue;
            var category = item.ItemUICategory.Value;
            rows.Add(new CatalogItem(item.RowId, item.Name.ToString(), item.ItemUICategory.RowId,
                category.Name.ToString()));
        }
        return rows;
    }

    public IReadOnlyList<InventoryCandidate> Scan(bool includeInventory, bool includeArmory)
    {
        var result = new List<InventoryCandidate>();
        var containers = new List<GameInventoryType>();
        if (includeInventory) containers.AddRange(InventoryContainers);
        if (includeArmory) containers.AddRange(ArmoryContainers);

        foreach (var container in containers)
        {
            foreach (var slot in inventory.GetInventoryItems(container))
            {
                if (slot.IsEmpty || slot.BaseItemId == 0 || slot.Quantity <= 0) continue;
                if (!dataManager.GetExcelSheet<Item>().TryGetRow(slot.BaseItemId, out var row)) continue;
                var category = row.ItemUICategory.Value.Name.ToString();
                result.Add(new InventoryCandidate(slot.ItemId, slot.BaseItemId, row.Name.ToString(),
                    category, slot.Quantity, container, slot.InventorySlot, slot.IsHq));
            }
        }
        return result;
    }

    public unsafe bool IsStillSameItem(InventoryCandidate candidate)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        var container = manager->GetInventoryContainer((InventoryType)candidate.Container);
        if (container == null || candidate.Slot >= container->Size) return false;
        var item = container->GetInventorySlot((int)candidate.Slot);
        return item != null && item->ItemId == candidate.ItemId && item->Quantity > 0;
    }

    public unsafe void RequestNativeDiscard(InventoryCandidate candidate)
    {
        var manager = InventoryManager.Instance();
        if (manager == null) throw new InvalidOperationException("Inventory manager is unavailable.");
        var container = manager->GetInventoryContainer((InventoryType)candidate.Container);
        if (container == null || candidate.Slot >= container->Size)
            throw new InvalidOperationException("The inventory slot is no longer valid.");
        var item = container->GetInventorySlot((int)candidate.Slot);
        if (item == null || item->ItemId != candidate.ItemId || item->Quantity <= 0)
            throw new InvalidOperationException("The item moved or changed after the scan.");

        AgentInventoryContext.Instance()->DiscardItem(item, item->Container, item->Slot, 0);
    }
}
