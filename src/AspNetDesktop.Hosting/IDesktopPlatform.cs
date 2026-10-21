using Microsoft.Extensions.Logging;

namespace AspNetDesktop.Hosting;

/// <summary>
/// A platform adapter: owns the native window and system WebView and forwards <c>app://</c> and <c>ipc://</c>
/// requests to <see cref="DesktopPlatformContext.RequestHandler"/>.
/// </summary>
public interface IDesktopPlatform
{
    /// <summary>Short platform name published to JavaScript as <c>__ASPNETDESKTOP__.platform</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Opens the window and runs the native message loop. Blocks until the window is closed
    /// or <see cref="DesktopPlatformContext.ApplicationStopping"/> fires.
    /// </summary>
    void Run(DesktopPlatformContext context);
}

/// <summary>
/// Everything a platform adapter needs from the host.
/// </summary>
public sealed class DesktopPlatformContext
{
    /// <summary>Creates the context.</summary>
    public DesktopPlatformContext(
        IServiceProvider services,
        IDesktopRequestHandler requestHandler,
        DesktopHostSettings settings,
        ILoggerFactory loggerFactory,
        CancellationToken applicationStopping)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(requestHandler);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        Services = services;
        RequestHandler = requestHandler;
        Settings = settings;
        LoggerFactory = loggerFactory;
        ApplicationStopping = applicationStopping;
    }

    /// <summary>The application services.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The in-process server; the only path from the WebView into ASP.NET Core.</summary>
    public IDesktopRequestHandler RequestHandler { get; }

    /// <summary>The effective desktop configuration (start URL, origins, navigation policy, ...).</summary>
    public DesktopHostSettings Settings { get; }

    /// <summary>Creates loggers for the adapter.</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>Signalled when the host is shutting down; the adapter must close its window.</summary>
    public CancellationToken ApplicationStopping { get; }

    /// <summary>The script to inject at document start; publishes <c>window.__ASPNETDESKTOP__</c>.</summary>
    public string GetBootstrapScript(IDesktopPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return Settings.CreateBootstrapScript(platform.Name);
    }
}
