using Dalamud.Interface.Windowing;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace InventoryDiscardAssistant;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Configuration config;
    private readonly Action save;
    private readonly InventoryScanner scanner;
    private readonly IClientState clientState;
    private List<InventoryCandidate> candidates = [];
    private string status = "Scan your inventory to list candidates.";
    private bool showPreview;
    private bool showCategoryConsumables = true;
    private bool showCategoryMaterials = true;
    private bool showCategoryOther = true;

    public MainWindow(Configuration config, Action save, IGameInventory inventory, IDataManager data, IClientState clientState)
        : base("Inventory Discard Assistant")
    {
        this.config = config;
        this.save = save;
        this.clientState = clientState;
        scanner = new InventoryScanner(inventory, data);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new System.Numerics.Vector2(560, 420), MaximumSize = new System.Numerics.Vector2(1000, 900) };
    }

    public void Toggle() => IsOpen = !IsOpen;

    public override void Draw()
    {
        ImGui.TextWrapped("Manual inventory cleanup. Nothing is discarded automatically in the background.");
        ImGui.Separator();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enable Auto-Discard (master switch)", ref enabled))
        {
            config.Enabled = enabled;
            save();
        }
        var preview = config.RequirePreview;
        if (ImGui.Checkbox("Require Preview Before Discarding", ref preview))
        {
            config.RequirePreview = preview;
            save();
        }

        ImGui.TextDisabled("Master switch OFF blocks all discard execution. Preview is ON by default.");
        ImGui.Separator();

        var inv = config.ScanInventory;
        if (ImGui.Checkbox("Scan player inventory", ref inv)) { config.ScanInventory = inv; save(); }
        var armory = config.ScanArmory;
        ImGui.SameLine();
        if (ImGui.Checkbox("Scan armory (not yet implemented)", ref armory))
        {
            config.ScanArmory = false;
            save();
        }

        ImGui.Text("Item filters");
        ImGui.Checkbox("Consumables", ref showCategoryConsumables);
        ImGui.SameLine();
        ImGui.Checkbox("Materials", ref showCategoryMaterials);
        ImGui.SameLine();
        ImGui.Checkbox("Other", ref showCategoryOther);

        var query = config.SearchText;
        if (ImGui.InputText("Search item names", ref query, 128))
        {
            config.SearchText = query;
            save();
        }

        if (ImGui.Button("Scan Inventory"))
        {
            candidates = scanner.Scan(config.ScanInventory, false)
                .Where(x => !config.ExcludedItemIds.Contains(x.ItemId))
                .Where(x => config.IncludedItemIds.Count == 0 || config.IncludedItemIds.Contains(x.ItemId))
                .Where(x => string.IsNullOrWhiteSpace(config.SearchText) || x.Name.Contains(config.SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();
            status = $"Found {candidates.Count} matching inventory stacks.";
            showPreview = true;
        }

        ImGui.Separator();
        ImGui.Text($"Matching stacks: {candidates.Count}");
        if (ImGui.BeginChild("candidate-list", new System.Numerics.Vector2(0, 220), true))
        {
            foreach (var item in candidates)
                ImGui.Text($"{item.Name} × {item.Quantity}  [{item.Container}, slot {item.Slot}]");
        }
        ImGui.EndChild();

        if (config.RequirePreview)
            ImGui.TextWrapped("Preview-before-discard is enabled. The list above is a scan preview; a second, final confirmation step is required before any item can be discarded.");

        ImGui.TextColored(new System.Numerics.Vector4(1f, 0.75f, 0.25f, 1f),
            "Discard execution is intentionally unavailable in this build. The native discard call and per-item confirmation flow have not yet passed API/build verification.");
        ImGui.Text(status);
    }

    public void Dispose() { }
}
