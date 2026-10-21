using AspNetDesktop.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Runs a desktop host in a native window on the current operating system.
/// </summary>
public static class DesktopApplicationExtensions
{
    /// <summary>
    /// Starts the host and shows the application window, blocking until the window is closed.
    /// The adapter is chosen by the app's target framework: <c>net10.0-windows</c> uses WebView2,
    /// <c>net10.0-macos</c> uses WKWebView and <c>net10.0</c> on Linux uses WebKitGTK.
    /// An <see cref="IDesktopPlatform"/> registered in the service collection takes precedence.
    /// </summary>
    public static void RunDesktop(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        IDesktopPlatform platform = host.Services.GetService<IDesktopPlatform>() ?? CreateDefaultPlatform();
        host.RunDesktop(platform);
    }

    /// <summary>Creates the adapter for the current target framework and operating system.</summary>
    public static IDesktopPlatform CreateDefaultPlatform()
    {
#if WINDOWS
        return new AspNetDesktop.Windows.WindowsDesktopPlatform();
#elif MACOS
        return new AspNetDesktop.Mac.MacDesktopPlatform();
#else
        if (OperatingSystem.IsLinux())
        {
            return new AspNetDesktop.Linux.LinuxDesktopPlatform();
        }

        string targetFramework = OperatingSystem.IsWindows() ? "net10.0-windows" : "net10.0-macos";
        throw new PlatformNotSupportedException(
            $"The net10.0 build of AspNetDesktop targets Linux (WebKitGTK). Target {targetFramework} to run on this operating system.");
#endif
    }
}
