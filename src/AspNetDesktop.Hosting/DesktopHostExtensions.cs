using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Runs a host configured with <c>AddDesktopHost()</c> inside a native window.
/// </summary>
public static class DesktopHostExtensions
{
    /// <summary>
    /// Returns the <see cref="DesktopServer"/> of a host configured with <c>AddDesktopHost()</c>.
    /// </summary>
    public static DesktopServer GetDesktopServer(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Services.GetService<IServer>() as DesktopServer
            ?? throw new InvalidOperationException(
                "The host is not using the desktop server. Call builder.AddDesktopHost() before building the application.");
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> that calls the started host in-process (see <see cref="DesktopServer.CreateClient"/>).
    /// </summary>
    public static HttpClient CreateDesktopClient(this IHost host, DesktopScheme scheme = DesktopScheme.Ipc) =>
        host.GetDesktopServer().CreateClient(scheme);

    /// <summary>
    /// Starts the host, runs <paramref name="platform"/> until its window closes, then stops the host.
    /// Closing the window stops the application; stopping the application closes the window.
    /// </summary>
    public static void RunDesktop(this IHost host, IDesktopPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(platform);

        DesktopServer server = host.GetDesktopServer();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        ILoggerFactory loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        host.StartAsync().GetAwaiter().GetResult();
        try
        {
            DesktopPlatformContext context = new(host.Services, server, server.Settings, loggerFactory, lifetime.ApplicationStopping);
            platform.Run(context);
        }
        finally
        {
            lifetime.StopApplication();
            host.StopAsync().GetAwaiter().GetResult();
            if (host is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else
            {
                host.Dispose();
            }
        }
    }
}
