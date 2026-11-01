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
    private List<InventoryCandidate> preview = [];
    private readonly HashSet<(GameInventoryType Container, uint Slot)> selectedStacks = [];
    private string status = "Choose item categories or individual items, then scan.";
    private bool catalogLoaded;
    private bool previewOpen;
    private bool awaitingNativeConfirmation;
    private List<InventoryCandidate> executionQueue = [];
    private int executionIndex;
    private InventoryCandidate? awaitingCandidate;

    public MainWindow(Configuration config, IGameInventory inventory, IDataManager data, ITextureProvider textures, IPluginLog log)
        : base("Inventory Discard Assistant")
    {
        this.config = config;
        this.log = log;
        this.textures = textures;
        scanner = new InventoryScanner(inventory, data);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 560),
            MaximumSize = new Vector2(1200, 1000)
        };
    }

    public void Toggle() => IsOpen = !IsOpen;

    public override void Draw()
    {
        AdvanceExecution();
        ImGui.TextWrapped("Nothing runs in the background. You must start each discard batch here, and the game will ask you to confirm every item.");
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

        ImGui.TextDisabled("Master switch OFF blocks execution. Preview is ON by default. The game's native Yes/No prompt is never bypassed.");
        ImGui.Separator();

        var scanInventory = config.ScanInventory;
        if (ImGui.Checkbox("Player inventory", ref scanInventory))
        {
            config.ScanInventory = scanInventory;
            config.Save();
        }
        ImGui.SameLine();
        var scanArmory = config.ScanArmory;
        if (ImGui.Checkbox("Armoury chest", ref scanArmory))
        {
            config.ScanArmory = scanArmory;
            config.Save();
        }

        EnsureCatalog();
        DrawFilters();
        ImGui.Separator();

        var scanQuery = config.SearchText;
        if (ImGui.InputText("Filter scanned item names", ref scanQuery, 128))
        {
            config.SearchText = scanQuery;
            config.Save();
        }

        if (ImGui.Button("Scan Inventory"))
            ScanCandidates();

        ImGui.SameLine();
        ImGui.Text($"Preview stacks: {preview.Count}");

        if (config.RequirePreview && preview.Count > 0)
        {
            if (ImGui.Button("Review Preview"))
                previewOpen = true;
        }

        if (!config.RequirePreview)
        {
            ImGui.SameLine();
            if (ImGui.Button("Discard Matching Items"))
                BeginExecution(preview);
        }

        DrawPreview();
        DrawExecutionStatus();
        ImGui.TextWrapped(status);
    }

    private void EnsureCatalog()
    {
        if (catalogLoaded) return;
        try
        {
            catalog = scanner.GetCatalog().OrderBy(x => x.CategoryName).ThenBy(x => x.Name).ToList();
            catalogLoaded = true;
        }
        catch (Exception ex)
        {
            status = $"Could not load item database: {ex.Message}";
            log.Error(ex, "Failed to load item catalog");
        }
    }

    private void DrawFilters()
    {
        if (ImGui.CollapsingHeader("Item categories", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var groups = catalog.GroupBy(x => new { x.CategoryId, x.CategoryName })
                .Where(g => g.Key.CategoryId != 0)
                .OrderBy(g => g.Key.CategoryName);
            foreach (var group in groups)
            {
                var selected = config.SelectedCategoryIds.Contains(group.Key.CategoryId);
                if (ImGui.Checkbox($"{group.Key.CategoryName} ({group.Count()})##cat{group.Key.CategoryId}", ref selected))
                {
                    if (selected) config.SelectedCategoryIds.Add(group.Key.CategoryId);
                    else config.SelectedCategoryIds.Remove(group.Key.CategoryId);
                    config.Save();
                }
            }
        }

        if (ImGui.CollapsingHeader("Individual item database"))
        {
            var query = config.CatalogSearchText;
            if (ImGui.InputText("Find item##catalog", ref query, 128))
            {
                config.CatalogSearchText = query;
                config.Save();
            }

            var matches = catalog.Where(x => string.IsNullOrWhiteSpace(query) ||
                x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(250).ToList();

            ImGui.TextDisabled($"Showing up to 250 matches. Check Include to select an item; Protect always overrides selection.");
            var child = ImGui.BeginChild("catalog-items", new Vector2(0, 180), true);
            if (child)
            {
                foreach (var item in matches)
                {
                    ImGui.PushID((int)item.ItemId);
                    var included = config.SelectedItemIds.Contains(item.ItemId);
                    if (ImGui.Checkbox("Include", ref included))
                    {
                        if (included) config.SelectedItemIds.Add(item.ItemId);
                        else config.SelectedItemIds.Remove(item.ItemId);
                        config.Save();
                    }
                    ImGui.SameLine();
                    var protectedItem = config.ExcludedItemIds.Contains(item.ItemId);
                    if (ImGui.Checkbox("Protect / favorite", ref protectedItem))
                    {
                        if (protectedItem) config.ExcludedItemIds.Add(item.ItemId);
                        else config.ExcludedItemIds.Remove(item.ItemId);
                        config.Save();
                    }
                    var icon = textures.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
                    if (icon != null) ImGui.Image(icon.Handle, new Vector2(20, 20));
                    ImGui.SameLine();
                    ImGui.Text($"{item.Name}  ({item.CategoryName})");
                    ImGui.PopID();
                }
            }
            ImGui.EndChild();
        }

        ImGui.Text($"Selected item IDs: {config.SelectedItemIds.Count} | Categories: {config.SelectedCategoryIds.Count} | Protected IDs: {config.ExcludedItemIds.Count}");
    }

    private void ScanCandidates()
    {
        if (!config.ScanInventory && !config.ScanArmory)
        {
            status = "Select at least one inventory location to scan.";
            preview.Clear();
            return;
        }

        if (config.SelectedItemIds.Count == 0 && config.SelectedCategoryIds.Count == 0)
        {
            status = "No item filters selected. Select one or more categories or individual items first.";
            preview.Clear();
            return;
        }

        try
        {
            var gearsetIds = GetGearsetItemIds();
            preview = scanner.Scan(config.ScanInventory, config.ScanArmory)
                .Where(x => !config.ExcludedItemIds.Contains(x.BaseItemId) && !config.ExcludedItemIds.Contains(x.ItemId))
                .Where(x => config.SelectedItemIds.Contains(x.BaseItemId) || config.SelectedCategoryIds.Contains(x.CategoryId))
                .Where(x => !gearsetIds.Contains(x.BaseItemId) && !gearsetIds.Contains(x.ItemId))
                .Where(x => string.IsNullOrWhiteSpace(config.SearchText) || x.Name.Contains(config.SearchText, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name).ToList();

            selectedStacks.Clear();
            foreach (var item in preview)
                selectedStacks.Add((item.Container, item.Slot));
            status = $"Scan complete: {preview.Count} matching stacks. Exclusions and gearset protection were applied.";
            previewOpen = config.RequirePreview;
        }
        catch (Exception ex)
        {
            status = $"Inventory scan failed: {ex.Message}";
            log.Error(ex, "Inventory scan failed");
        }
    }

    private unsafe HashSet<uint> GetGearsetItemIds()
    {
        var result = new HashSet<uint>();
        var module = RaptureGearsetModule.Instance();
        if (module == null) throw new InvalidOperationException("Gearset data is unavailable; scan cancelled to avoid discarding gearset items.");

        for (var i = 0; i < 100; i++)
        {
            var gearset = module->GetGearset(i);
            if (gearset == null || !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;

            var indices = new[]
            {
                RaptureGearsetModule.GearsetItemIndex.MainHand,
                RaptureGearsetModule.GearsetItemIndex.OffHand,
                RaptureGearsetModule.GearsetItemIndex.Head,
                RaptureGearsetModule.GearsetItemIndex.Body,
                RaptureGearsetModule.GearsetItemIndex.Hands,
                RaptureGearsetModule.GearsetItemIndex.Legs,
                RaptureGearsetModule.GearsetItemIndex.Feet,
                RaptureGearsetModule.GearsetItemIndex.Ears,
                RaptureGearsetModule.GearsetItemIndex.Neck,
                RaptureGearsetModule.GearsetItemIndex.Wrists,
                RaptureGearsetModule.GearsetItemIndex.RingLeft,
                RaptureGearsetModule.GearsetItemIndex.RingRight,
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
        if (!previewOpen) return;
        ImGui.Separator();
        ImGui.Text("Discard preview — uncheck any stack you want to keep.");
        var previewChild = ImGui.BeginChild("preview-items", new Vector2(0, 180), true);
        if (previewChild)
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
                    if (icon != null) ImGui.Image(icon.Handle, new Vector2(24, 24));
                    ImGui.SameLine();
                    ImGui.Text($"{item.Name} × {item.Quantity} {(item.IsHq ? "(HQ)" : "")} — {item.Container}, slot {item.Slot}");
                    ImGui.PopID();
                }
        }
        ImGui.EndChild();

        ImGui.Text($"Selected for this batch: {selectedStacks.Count}");
        if (ImGui.Button("Confirm Preview & Start"))
            BeginExecution(preview.Where(x => selectedStacks.Contains((x.Container, x.Slot))).ToList());
        ImGui.SameLine();
        if (ImGui.Button("Cancel Preview"))
            previewOpen = false;
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
        if (!config.Enabled)
        {
            StopExecution("Master switch disabled; batch stopped.");
            return;
        }
        if (executionIndex >= executionQueue.Count)
        {
            StopExecution("Batch complete.");
            return;
        }

        var candidate = executionQueue[executionIndex];
        if (config.ExcludedItemIds.Contains(candidate.BaseItemId) ||
            config.ExcludedItemIds.Contains(candidate.ItemId) ||
            GetGearsetItemIds().Contains(candidate.BaseItemId) ||
            GetGearsetItemIds().Contains(candidate.ItemId))
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

            // This requests the game's own discard action. The plugin does not click,
            // accept, or otherwise bypass the native Yes/No confirmation dialog.
            scanner.RequestNativeDiscard(candidate);
            awaitingCandidate = candidate;
            awaitingNativeConfirmation = true;
            status = $"Waiting for you to confirm in the game's native dialog: {candidate.Name}.";
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
        if (!config.Enabled)
        {
            StopExecution("Master switch disabled; batch stopped.");
            return;
        }

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
        if (ImGui.Button("Stop Batch"))
            StopExecution("Batch stopped by user.");
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
