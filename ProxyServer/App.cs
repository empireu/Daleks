using GameFramework;
using GameFramework.ImGui;
using ImGuiNET;
using Veldrid;

namespace ProxyServer;

internal class App : GameApplication
{
    private readonly Server _server;

    public App(Server server)
    {
        _server = server;
    }

    protected override void Initialize()
    {
        Layers.ConstructLayer<ImGuiLayer>(imGui =>
        {
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ImGuiStyles.Dark();
            ImGui.LoadIniSettingsFromDisk("imgui_server.ini");
            imGui.Submit += ImGuiOnSubmit;
        });
    }

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        
    }

    protected override void Destroy()
    {
        ImGui.SaveIniSettingsToDisk("imgui_server.ini");

        base.Destroy();
    }
}