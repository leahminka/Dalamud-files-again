using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace InventoryDiscardAssistant;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IGameInventory Inventory { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    private readonly Configuration configuration;
    private readonly MainWindow window;
    private readonly WindowSystem windowSystem = new("InventoryDiscardAssistant");

    public string Name => "Inventory Discard Assistant";

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.SelectedItemIds ??= [];
        configuration.AutoSelectedItemIds ??= [];
        configuration.ManuallyDeselectedItemIds ??= [];
        configuration.ExcludedItemIds ??= [];
        if (!configuration.RememberManualDeselections)
        {
            foreach (var itemId in configuration.ManuallyDeselectedItemIds)
            {
                configuration.AutoSelectedItemIds.Remove(itemId);
                configuration.SelectedItemIds.Remove(itemId);
            }
            configuration.ManuallyDeselectedItemIds.Clear();
        }
        configuration.SelectedCategoryIds ??= [];

        window = new MainWindow(configuration, Inventory, DataManager, TextureProvider, Log)
        {
            IsOpen = false,
            ShowCloseButton = true,
        };
        windowSystem.AddWindow(window);

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += window.Toggle;

        CommandManager.AddHandler("/aid", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggles the Inventory Discard Assistant window.",
        });
    }

    private void OnCommand(string command, string arguments) => window.Toggle();

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= window.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= window.Toggle;
        CommandManager.RemoveHandler("/aid");
        windowSystem.RemoveAllWindows();
        window.Dispose();
    }
}
