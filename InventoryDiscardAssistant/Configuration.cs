using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace InventoryDiscardAssistant;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public bool DisableHoldCtrlToDiscard { get; set; }
    public bool IncludeCollectibles { get; set; } = true;
    public bool ScanInventory { get; set; } = true;
    public bool ScanArmory { get; set; }
    public bool ScanSaddlebag { get; set; }
    public string SearchText { get; set; } = string.Empty;
    public string CatalogSearchText { get; set; } = string.Empty;
    public float InventoryPanelWidth { get; set; } = 520f;
    public HashSet<uint> SelectedItemIds { get; set; } = [];
    public HashSet<uint> AutoSelectedItemIds { get; set; } = [];
    public HashSet<uint> ManuallyDeselectedItemIds { get; set; } = [];
    public HashSet<uint> ExcludedItemIds { get; set; } = [];
    public HashSet<uint> SelectedCategoryIds { get; set; } = [];
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
