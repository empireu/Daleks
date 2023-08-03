using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ProxyServer;

internal static class Extensions
{
    public static IHostBuilder UseGameServer(this IHostBuilder builder)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddHostedService<Server>();
        });
    }
}