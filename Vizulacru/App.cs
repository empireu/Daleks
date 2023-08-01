using GameFramework;
using GameFramework.Assets;
using GameFramework.ImGui;
using ImGuiNET;
using Microsoft.Extensions.DependencyInjection;
using Veldrid;

namespace Vizulacru;

internal sealed class App : GameApplication
{
    private readonly IServiceProvider _serviceProvider;
    private int _selectedId;

    public int SelectedId => _selectedId;

    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Device.SyncToVerticalBlank = true;
        Window.Title = "hublou";
        ClearColor = RgbaFloat.Black;
    }

    protected override IServiceProvider BuildLayerServiceProvider(ServiceCollection registeredServices)
    {
        return _serviceProvider;
    }

    protected override void Initialize()
    {
        Layers.ConstructLayer<ImGuiLayer>(imGui =>
        {
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ImGuiStyles.Dark();
            ImGui.LoadIniSettingsFromDisk("imgui_vizulacru.ini");
            imGui.Submit += ImGuiOnSubmit;
        });
    }

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        if (!Layers.FrontToBack.Any(t => t is WorldLayer))
        {
            if (ImGuiExt.Begin("App", "Session"))
            {
                ImGui.SliderInt("ID", ref _selectedId, 0, 5);
            }

            if (ImGui.Button("Start"))
            {
                Layers.AddLayer(ActivatorUtilities.CreateInstance<WorldLayer>(_serviceProvider));
            }

            ImGui.End();
        }
    }

    public static EmbeddedResourceKey Asset(string name)
    {
        return new EmbeddedResourceKey(typeof(App).Assembly, $"Vizulacru.Assets.{name}");
    }

    protected override void Destroy()
    {
        ImGui.SaveIniSettingsToDisk("imgui_vizulacru.ini");

        base.Destroy();
    }
}