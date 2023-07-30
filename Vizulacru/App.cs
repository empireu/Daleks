using System.Diagnostics;
using GameFramework;
using GameFramework.Assets;
using GameFramework.ImGui;
using GameFramework.Layers;
using ImGuiNET;
using Microsoft.Extensions.DependencyInjection;
using Veldrid;

namespace Vizulacru;

internal sealed class App : GameApplication
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Device.SyncToVerticalBlank = true;
        Window.Title = "geam";
        ClearColor = RgbaFloat.Black;
    }

    protected override IServiceProvider BuildLayerServiceProvider(ServiceCollection registeredServices)
    {
        return _serviceProvider;
    }

    protected override void Initialize()
    {
        Window.Title = "Coyote";

        Layers.ConstructLayer<ImGuiLayer>(imGui =>
        {
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ImGuiStyles.Dark();

            ImGui.LoadIniSettingsFromDisk("imgui.ini");

            imGui.Submit += ImGuiOnSubmit;
            imGui.EnableStats = true;
        });

        Layers.ConstructLayer<WorldLayer>();
    }

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        ImGui.ShowDemoWindow();
    }

    public static EmbeddedResourceKey Asset(string name)
    {
        return new EmbeddedResourceKey(typeof(App).Assembly, $"Vizulacru.Assets.{name}");
    }
}