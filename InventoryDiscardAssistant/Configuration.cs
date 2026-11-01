using Dalamud.Configuration;
using System.Collections.Generic;

namespace InventoryDiscardAssistant;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool RequirePreview { get; set; } = true;
    public bool ScanInventory { get; set; } = true;
    public bool ScanArmory { get; set; }
    public bool IncludeHighQuality { get; set; } = true;
    public bool IncludeLowQuality { get; set; } = true;
    public string SearchText { get; set; } = string.Empty;
    public HashSet<uint> IncludedItemIds { get; set; } = [];
    public HashSet<uint> ExcludedItemIds { get; set; } = [];
}
