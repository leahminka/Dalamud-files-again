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
    private List<InventoryCandidate> scannedItems = [];
    private readonly HashSet<(GameInventoryType Container, uint Slot)> excludedStacks = [];
    private string status = "Press Scan Personal Inventory to load the items in your inventory.";
    private bool awaitingNativeConfirmation;
    private bool hasScanned;
    private List<InventoryCandidate> executionQueue = [];
    private int executionIndex;
    private InventoryCandidate? awaitingCandidate;
    private string selectedCategory = string.Empty;

    private sealed record ItemSummary(uint ItemId, uint IconId, string Name, string Category,
        int Quantity, int StackCount);

    public MainWindow(Configuration config, IGameInventory inventory, IDataManager data, ITextureProvider textures, IPluginLog log)
        : base("Inventory Discard Assistant")
    {
        this.config = config;
        this.log = log;
        this.textures = textures;
        scanner = new InventoryScanner(inventory, data);
        ShowCloseButton = true;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 600),
            MaximumSize = new Vector2(2400, 1800)
        };
    }

    public override void Draw()
    {
        AdvanceExecution();

        const string decorativeText = "yuff is cute";
        var decorativeSize = ImGui.CalcTextSize(decorativeText);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetWindowWidth() - decorativeSize.X) / 2f));
        ImGui.TextColored(new Vector4(0.38f, 0.68f, 1.0f, 1.0f), decorativeText);

        ImGui.TextWrapped("Scan selected storage, choose items, then review every stack before discarding. Nothing runs in the background.");
        DrawScanOptions();

        var disableCtrl = config.DisableHoldCtrlToDiscard;
        if (ImGui.Checkbox("Disable holding Ctrl to discard", ref disableCtrl))
        {
            config.DisableHoldCtrlToDiscard = disableCtrl;
            config.Save();
        }
        ImGui.SameLine();
        var includeCollectibles = config.IncludeCollectibles;
        if (ImGui.Checkbox("Include Collectibles", ref includeCollectibles))
        {
            config.IncludeCollectibles = includeCollectibles;
            config.Save();
            if (hasScanned) ScanCandidates();
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

        if (ImGui.Button("Scan Selected Locations"))
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

    }

    private void DrawScanOptions()
    {
        ImGui.Text("Scan locations");
        var personal = config.ScanInventory;
        if (ImGui.Checkbox("Personal Inventory", ref personal))
        {
            config.ScanInventory = personal;
            config.Save();
        }
        ImGui.SameLine();
        var armory = config.ScanArmory;
        if (ImGui.Checkbox("Armoury Chest", ref armory))
        {
            config.ScanArmory = armory;
            config.Save();
        }
        ImGui.SameLine();
        var saddlebag = config.ScanSaddlebag;
        if (ImGui.Checkbox("Chocobo Saddlebag", ref saddlebag))
        {
            config.ScanSaddlebag = saddlebag;
            config.Save();
        }
        ImGui.SameLine();
        ImGui.BeginDisabled();
        var dresser = false;
        ImGui.Checkbox("Glamour Dresser (not supported)", ref dresser);
        ImGui.EndDisabled();
        ImGui.TextDisabled("Saddlebag includes Premium Saddlebag when available. Glamour Dresser contents are not exposed as a safe discardable inventory by this API.");
    }

    private void ScanCandidates()
    {
        try
        {
            scannedItems = scanner.Scan(config.ScanInventory, config.ScanArmory, config.ScanSaddlebag, config.IncludeCollectibles).ToList();
            hasScanned = true;
            status = scannedItems.Count == 0
                ? "Scan complete: no items were found in the selected inventory locations."
                : $"Scan complete: found {scannedItems.Count} item stacks. Toggle items below to select them.";
            excludedStacks.Clear();
        }
        catch (Exception ex)
        {
            scannedItems.Clear();
            hasScanned = false;
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
        ImGui.Text($"Scanned items ({GetVisibleSummaries().Count} unique items)");
        ImGui.Separator();
        var summaries = GetVisibleSummaries();
        if (scannedItems.Count == 0)
        {
            ImGui.TextWrapped("No items found. Check the scan locations above, then press Scan Selected Locations.");
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
        var selected = GetSelectedCandidates();
        ImGui.Text($"Selected to discard ({selected.Count} stacks)");
        DrawCategorySelector();
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
            excludedStacks.Clear();
            config.Save();
        }

        var listHeight = Math.Max(100, ImGui.GetContentRegionAvail().Y - 120);
        if (ImGui.BeginChild("selected-items-list", new Vector2(0, listHeight), false))
        {
            foreach (var item in selected)
            {
                ImGui.PushID($"{(uint)item.Container}:{item.Slot}");
                var includeStack = !excludedStacks.Contains((item.Container, item.Slot));
                if (ImGui.Checkbox("##discard-stack", ref includeStack))
                {
                    if (includeStack) excludedStacks.Remove((item.Container, item.Slot));
                    else excludedStacks.Add((item.Container, item.Slot));
                }
                ImGui.SameLine();
                var icon = textures.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(22, 22));
                    ImGui.SameLine();
                }
                ImGui.BeginGroup();
                ImGui.Text($"{item.Name} × {item.Quantity:N0} {(item.IsHq ? "(HQ)" : "")}");
                ImGui.TextDisabled($"{item.Container}, slot {item.Slot}");
                ImGui.EndGroup();
                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        var includedCount = selected.Count(x => !excludedStacks.Contains((x.Container, x.Slot)));
        ImGui.TextDisabled($"Stacks selected: {includedCount} / {selected.Count}");
        var requiresCtrl = !config.DisableHoldCtrlToDiscard;
        var buttonLabel = requiresCtrl ? "Hold Ctrl + Click to Confirm Discard" : "Confirm Discard";
        ImGui.BeginDisabled(includedCount == 0 || awaitingNativeConfirmation || executionQueue.Count > 0);
        if (ImGui.Button(buttonLabel, new Vector2(-1, 0)))
        {
            if (requiresCtrl && !ImGui.GetIO().KeyCtrl)
                status = "Confirmation not started. Hold Ctrl while clicking Confirm Discard.";
            else
                BeginExecution(selected.Where(x => !excludedStacks.Contains((x.Container, x.Slot))));
        }
        ImGui.EndDisabled();
        if (requiresCtrl)
            ImGui.TextDisabled("Hold Ctrl while clicking to start the discard batch.");

        ImGui.TextDisabled($"Protected item types: {config.ExcludedItemIds.Count}");
        DrawExecutionStatus();
    }
    private void DrawCategorySelector()
    {
        // Keep every supported category available even if the current scan has no matches.
        var categories = new[]
        {
            "Fish", "Monster Materials", "Crafting Materials", "Dyes", "Miscellaneous"
        }.Concat(GetVisibleSummaries().Select(x => x.Category))
         .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();

        ImGui.TextDisabled("Bulk select by category");
        ImGui.SetNextItemWidth(220);
        var comboLabel = string.IsNullOrEmpty(selectedCategory) ? "Choose a category..." : selectedCategory;
        if (ImGui.BeginCombo("##bulk-category", comboLabel))
        {
            foreach (var category in categories)
            {
                var isSelected = selectedCategory == category;
                if (ImGui.Selectable(category, isSelected))
                    selectedCategory = category;
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(string.IsNullOrEmpty(selectedCategory));
        if (ImGui.Button("Select All in Category"))
        {
            foreach (var item in GetVisibleSummaries().Where(x => x.Category == selectedCategory))
                if (!config.ExcludedItemIds.Contains(item.ItemId))
                    config.SelectedItemIds.Add(item.ItemId);
            config.Save();
        }
        ImGui.EndDisabled();
    }
    private List<InventoryCandidate> GetSelectedCandidates() =>
        scannedItems.Where(x => config.SelectedItemIds.Contains(x.BaseItemId) &&
            !config.ExcludedItemIds.Contains(x.BaseItemId) &&
            !config.ExcludedItemIds.Contains(x.ItemId)).ToList();

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

    private void BeginExecution(IEnumerable<InventoryCandidate> items)
    {
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
        RequestNextItem();
    }

    private void RequestNextItem()
    {
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
