using System.Numerics;
using Dalamud.Game.Inventory;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace InventoryDiscardAssistant;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly InventoryScanner scanner;
    private readonly IPluginLog log;
    private readonly ITextureProvider textures;
    private List<CatalogItem> catalog = [];
    private List<InventoryCandidate> scannedItems = [];
    private List<InventoryCandidate> preview = [];
    private readonly HashSet<(GameInventoryType Container, uint Slot)> selectedStacks = [];
    private string status = "Press Scan Inventory to load the items in your personal inventory.";
    private bool catalogLoaded;
    private bool previewOpen;
    private bool awaitingNativeConfirmation;
    private List<InventoryCandidate> executionQueue = [];
    private int executionIndex;
    private InventoryCandidate? awaitingCandidate;

    private sealed record ItemSummary(uint ItemId, uint IconId, string Name, string Category,
        int Quantity, int StackCount);

    public MainWindow(Configuration config, IGameInventory inventory, IDataManager data, ITextureProvider textures, IPluginLog log)
        : base("Inventory Discard Assistant")
    {
        this.config = config;
        this.log = log;
        this.textures = textures;
        scanner = new InventoryScanner(inventory, data);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 600),
            MaximumSize = new Vector2(2400, 1800)
        };
    }

    public new void Toggle() => IsOpen = !IsOpen;

    public override void Draw()
    {
        AdvanceExecution();
        ImGui.TextWrapped("Scan first, choose items from your actual inventory, then review before discarding. Nothing runs in the background.");
        ImGui.Separator();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Auto-Discard (master switch)", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
            if (!enabled) StopExecution("Master switch disabled.");
        }
        ImGui.SameLine();
        var previewRequired = config.RequirePreview;
        if (ImGui.Checkbox("Require Preview Before Discarding", ref previewRequired))
        {
            config.RequirePreview = previewRequired;
            config.Save();
        }

        ImGui.Separator();
        ImGui.Text("Search your inventory");
        ImGui.SetNextItemWidth(-1);
        var search = config.SearchText;
        if (ImGui.InputTextWithHint("##inventory-search", "Search items by name...", ref search, 128))
        {
            config.SearchText = search;
            config.Save();
        }

        if (ImGui.Button("Scan Personal Inventory"))
            ScanCandidates();
        ImGui.SameLine();
        ImGui.TextDisabled($"{scannedItems.Count} inventory stacks scanned");
        ImGui.SameLine();
        if (ImGui.Button("Clear Selection"))
        {
            config.SelectedItemIds.Clear();
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Select All Visible"))
        {
            foreach (var item in GetVisibleSummaries())
                if (!config.ExcludedItemIds.Contains(item.ItemId))
                    config.SelectedItemIds.Add(item.ItemId);
            config.Save();
        }

        ImGui.TextWrapped(status);
        ImGui.Separator();

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var availableHeight = Math.Max(180, ImGui.GetContentRegionAvail().Y);
        var leftWidth = Math.Clamp(config.InventoryPanelWidth, 260f, Math.Max(260f, availableWidth - 260f));

        ImGui.BeginChild("inventory-panel", new Vector2(leftWidth, availableHeight), true);
        DrawInventoryPanel();
        ImGui.EndChild();

        ImGui.SameLine(0, 0);
        ImGui.InvisibleButton("##inventory-panel-splitter", new Vector2(7, availableHeight));
        if (ImGui.IsItemActive())
        {
            config.InventoryPanelWidth = Math.Clamp(config.InventoryPanelWidth + ImGui.GetIO().MouseDelta.X,
                260f, Math.Max(260f, availableWidth - 260f));
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
        ImGui.SameLine(0, 0);

        ImGui.BeginChild("selection-panel", new Vector2(0, availableHeight), true);
        DrawSelectionPanel();
        ImGui.EndChild();

        DrawExecutionStatus();
        if (previewOpen) DrawPreview();
    }

    private void ScanCandidates()
    {
        try
        {
            // Always scan the four personal inventory bags first. No selection filters
            // are required to discover the contents of the player's inventory.
            scannedItems = scanner.Scan(includeInventory: true, includeArmory: config.ScanArmory).ToList();
            status = scannedItems.Count == 0
                ? "Scan complete: no items were found in the selected inventory locations."
                : $"Scan complete: found {scannedItems.Count} item stacks. Toggle items below to select them.";
            preview.Clear();
            selectedStacks.Clear();
            previewOpen = false;
        }
        catch (Exception ex)
        {
            scannedItems.Clear();
            status = $"Inventory scan failed: {ex.Message}";
            log.Error(ex, "Inventory scan failed");
        }
    }

    private List<ItemSummary> GetVisibleSummaries()
    {
        var filtered = scannedItems.Where(x => string.IsNullOrWhiteSpace(config.SearchText) ||
            x.Name.Contains(config.SearchText, StringComparison.OrdinalIgnoreCase));
        return filtered.GroupBy(x => x.BaseItemId).Select(group =>
        {
            var first = group.First();
            return new ItemSummary(first.BaseItemId, first.IconId, first.Name, first.Category,
                group.Sum(x => x.Quantity), group.Count());
        }).OrderBy(x => x.Name).ToList();
    }

    private void DrawInventoryPanel()
    {
        ImGui.Text($"Personal inventory ({GetVisibleSummaries().Count} unique items)");
        ImGui.Separator();
        var summaries = GetVisibleSummaries();
        if (scannedItems.Count == 0)
        {
            ImGui.TextWrapped("Your inventory list is empty. Press Scan Personal Inventory above to read your four personal inventory bags.");
            return;
        }

        if (summaries.Count == 0)
        {
            ImGui.TextWrapped("No inventory items match this search.");
            return;
        }

        if (ImGui.BeginChild("scanned-inventory-list", new Vector2(0, 0), false))
        {
            foreach (var item in summaries)
            {
                ImGui.PushID((int)item.ItemId);
                var protectedItem = config.ExcludedItemIds.Contains(item.ItemId);
                var included = config.SelectedItemIds.Contains(item.ItemId) && !protectedItem;

                if (ImGui.Checkbox("##include", ref included))
                {
                    if (included) config.SelectedItemIds.Add(item.ItemId);
                    else config.SelectedItemIds.Remove(item.ItemId);
                    config.Save();
                }
                ImGui.SameLine();

                var icon = textures.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(28, 28));
                    ImGui.SameLine();
                }

                ImGui.BeginGroup();
                ImGui.Text(item.Name);
                ImGui.TextDisabled($"{item.Quantity:N0} total  |  {item.StackCount} stack(s)  |  {item.Category}");
                ImGui.EndGroup();

                ImGui.SameLine();
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - 112));
                if (ImGui.SmallButton(protectedItem ? "Protected" : "Protect"))
                {
                    if (protectedItem) config.ExcludedItemIds.Remove(item.ItemId);
                    else
                    {
                        config.ExcludedItemIds.Add(item.ItemId);
                        config.SelectedItemIds.Remove(item.ItemId);
                    }
                    config.Save();
                }
                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void DrawSelectionPanel()
    {
        var selected = GetVisibleSelectedItems();
        ImGui.Text($"Selected to discard ({selected.Count})");
        ImGui.Separator();

        if (ImGui.Button("Select All"))
        {
            foreach (var item in GetVisibleSummaries())
                if (!config.ExcludedItemIds.Contains(item.ItemId))
                    config.SelectedItemIds.Add(item.ItemId);
            config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Deselect All"))
        {
            config.SelectedItemIds.Clear();
            config.Save();
        }

        if (ImGui.BeginChild("selected-items-list", new Vector2(0, Math.Max(100, ImGui.GetContentRegionAvail().Y - 80)), false))
        {
            foreach (var item in selected)
            {
                ImGui.PushID((int)item.ItemId);
                ImGui.Text(item.Name);
                ImGui.TextDisabled($"{item.Quantity:N0} total across {item.StackCount} stack(s)");
                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                {
                    config.SelectedItemIds.Remove(item.ItemId);
                    config.Save();
                }
                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        if (selected.Count > 0)
        {
            if (ImGui.Button("Build Discard Preview", new Vector2(-1, 0)))
            {
                BuildPreview();
            }
            if (!config.RequirePreview)
            {
                if (ImGui.Button("Discard Selected (skip preview)", new Vector2(-1, 0)))
                    BeginExecution(GetSelectedCandidates());
            }
        }
        ImGui.TextDisabled($"Protected item types: {config.ExcludedItemIds.Count}");
    }

    private List<ItemSummary> GetVisibleSelectedItems() =>
        GetVisibleSummaries().Where(x => config.SelectedItemIds.Contains(x.ItemId) &&
            !config.ExcludedItemIds.Contains(x.ItemId)).ToList();

    private List<InventoryCandidate> GetSelectedCandidates() =>
        scannedItems.Where(x => config.SelectedItemIds.Contains(x.BaseItemId) &&
            !config.ExcludedItemIds.Contains(x.BaseItemId) &&
            !config.ExcludedItemIds.Contains(x.ItemId)).ToList();

    private void BuildPreview()
    {
        preview = GetSelectedCandidates().Where(x => !GetGearsetItemIds().Contains(x.BaseItemId) &&
            !GetGearsetItemIds().Contains(x.ItemId)).ToList();
        selectedStacks.Clear();
        foreach (var item in preview) selectedStacks.Add((item.Container, item.Slot));
        previewOpen = true;
        status = $"Preview ready: {preview.Count} stacks selected. Review each stack before proceeding.";
    }

    private unsafe HashSet<uint> GetGearsetItemIds()
    {
        var result = new HashSet<uint>();
        var module = RaptureGearsetModule.Instance();
        if (module == null) throw new InvalidOperationException("Gearset data is unavailable; scan cancelled to avoid discarding gearset items.");

        for (var i = 0; i < 100; i++)
        {
            var gearset = module->GetGearset(i);
            if (gearset == null || !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) continue;
            var indices = new[]
            {
                RaptureGearsetModule.GearsetItemIndex.MainHand, RaptureGearsetModule.GearsetItemIndex.OffHand,
                RaptureGearsetModule.GearsetItemIndex.Head, RaptureGearsetModule.GearsetItemIndex.Body,
                RaptureGearsetModule.GearsetItemIndex.Hands, RaptureGearsetModule.GearsetItemIndex.Legs,
                RaptureGearsetModule.GearsetItemIndex.Feet, RaptureGearsetModule.GearsetItemIndex.Ears,
                RaptureGearsetModule.GearsetItemIndex.Neck, RaptureGearsetModule.GearsetItemIndex.Wrists,
                RaptureGearsetModule.GearsetItemIndex.RingLeft, RaptureGearsetModule.GearsetItemIndex.RingRight,
            };
            foreach (var index in indices)
            {
                var itemId = gearset->GetItem(index).ItemId;
                if (itemId != 0) result.Add(itemId);
            }
        }
        return result;
    }

    private void DrawPreview()
    {
        ImGui.Separator();
        ImGui.Text("Discard preview — uncheck any stack you want to keep.");
        if (ImGui.BeginChild("preview-items", new Vector2(0, 180), true))
        {
            foreach (var item in preview)
            {
                ImGui.PushID($"{(uint)item.Container}:{item.Slot}");
                var selected = selectedStacks.Contains((item.Container, item.Slot));
                if (ImGui.Checkbox("Discard", ref selected))
                {
                    if (selected) selectedStacks.Add((item.Container, item.Slot));
                    else selectedStacks.Remove((item.Container, item.Slot));
                }
                var icon = textures.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
                if (icon != null)
                {
                    ImGui.SameLine();
                    ImGui.Image(icon.Handle, new Vector2(22, 22));
                }
                ImGui.SameLine();
                ImGui.Text($"{item.Name} × {item.Quantity} {(item.IsHq ? "(HQ)" : "")} — {item.Container}, slot {item.Slot}");
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        ImGui.Text($"Selected stacks: {selectedStacks.Count}");
        if (ImGui.Button("Confirm Preview & Start"))
            BeginExecution(preview.Where(x => selectedStacks.Contains((x.Container, x.Slot))).ToList());
        ImGui.SameLine();
        if (ImGui.Button("Cancel Preview")) previewOpen = false;
    }

    private void BeginExecution(IEnumerable<InventoryCandidate> items)
    {
        if (!config.Enabled)
        {
            status = "Execution blocked: enable the master switch first.";
            return;
        }
        if (awaitingNativeConfirmation || executionQueue.Count > 0)
        {
            status = "A discard batch is already active. Stop it before starting another.";
            return;
        }

        executionQueue = items.ToList();
        executionIndex = 0;
        if (executionQueue.Count == 0)
        {
            status = "No stacks selected for discard.";
            return;
        }
        previewOpen = false;
        RequestNextItem();
    }

    private void RequestNextItem()
    {
        if (!config.Enabled) { StopExecution("Master switch disabled; batch stopped."); return; }
        if (executionIndex >= executionQueue.Count) { StopExecution("Batch complete."); return; }

        var candidate = executionQueue[executionIndex];
        var protectedIds = config.ExcludedItemIds;
        var gearsetIds = GetGearsetItemIds();
        if (protectedIds.Contains(candidate.BaseItemId) || protectedIds.Contains(candidate.ItemId) ||
            gearsetIds.Contains(candidate.BaseItemId) || gearsetIds.Contains(candidate.ItemId))
        {
            executionIndex++;
            RequestNextItem();
            return;
        }

        try
        {
            if (!scanner.IsStillSameItem(candidate))
            {
                executionIndex++;
                RequestNextItem();
                return;
            }
            scanner.RequestNativeDiscard(candidate);
            awaitingCandidate = candidate;
            awaitingNativeConfirmation = true;
            status = $"Waiting for your confirmation in the game's native dialog: {candidate.Name}.";
        }
        catch (Exception ex)
        {
            log.Error(ex, "Could not request native discard for {ItemName}", candidate.Name);
            executionIndex++;
            status = $"Skipped {candidate.Name}: {ex.Message}";
            RequestNextItem();
        }
    }

    private void AdvanceExecution()
    {
        if (!awaitingNativeConfirmation || awaitingCandidate == null) return;
        if (!config.Enabled) { StopExecution("Master switch disabled; batch stopped."); return; }
        if (!scanner.IsStillSameItem(awaitingCandidate))
        {
            awaitingNativeConfirmation = false;
            awaitingCandidate = null;
            executionIndex++;
            RequestNextItem();
        }
    }

    private void DrawExecutionStatus()
    {
        if (executionQueue.Count == 0 && !awaitingNativeConfirmation) return;
        ImGui.Separator();
        ImGui.Text($"Batch progress: {Math.Min(executionIndex + (awaitingNativeConfirmation ? 1 : 0), executionQueue.Count)} / {executionQueue.Count}");
        ImGui.TextWrapped("Accept or cancel each item using the game's own confirmation dialog. If you cancel a dialog, press Stop Batch here.");
        if (ImGui.Button("Stop Batch")) StopExecution("Batch stopped by user.");
    }

    private void StopExecution(string message)
    {
        executionQueue.Clear();
        executionIndex = 0;
        awaitingCandidate = null;
        awaitingNativeConfirmation = false;
        status = message;
    }

    public void Dispose() => StopExecution("Plugin unloaded.");
}
