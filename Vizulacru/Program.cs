using GameFramework;
using GameFramework.ImGui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Vizulacru;
using Vizulacru.Assets;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Verbose)
    .Enrich.FromLogContext()
    .WriteTo.File($"logs/logs-{DateTime.Now.ToString("s").Replace(":", ".")}.txt", LogEventLevel.Debug, rollingInterval: RollingInterval.Infinite)
    .WriteTo.Console()
    .CreateLogger();

using var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(Directory.GetCurrentDirectory())
    .UseSerilog()
    .UseConsoleLifetime()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ImGuiLayer>();
        services.AddSingleton<App>();
        services.AddSingleton<Textures>();
        services.AddSingleton<GameApplication>(s => s.GetRequiredService<App>());
    })
    .Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

host.StartAsync();

// Run graphics on the main thread:
host.Services.GetRequiredService<App>().Run();

// The application was closed:
lifetime.StopApplication();
host.WaitForShutdown();