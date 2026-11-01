using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace InventoryDiscardAssistant;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly Configuration configuration;
    private readonly MainWindow window;

    public string Name => "Inventory Discard Assistant";

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.SelectedItemIds ??= [];
        configuration.ExcludedItemIds ??= [];
        configuration.SelectedCategoryIds ??= [];
        window = new MainWindow(configuration, Inventory, DataManager, Log);
        PluginInterface.UiBuilder.Draw += window.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += window.Toggle;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= window.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= window.Toggle;
        window.Dispose();
    }
}
