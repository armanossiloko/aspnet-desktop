using AspNetDesktop.Hosting;
using AspNetDesktop.Hosting.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Registers the desktop host in place of Kestrel.
/// </summary>
public static class DesktopHostingExtensions
{
    /// <summary>
    /// Replaces Kestrel with the in-process <see cref="DesktopServer"/>: the pipeline is reachable only through the
    /// WebView's <c>app://</c> and <c>ipc://</c> schemes and never binds a TCP port.
    /// Options are bound from the <c>Desktop</c> configuration section and then from <paramref name="configure"/>.
    /// </summary>
    public static WebApplicationBuilder AddDesktopHost(this WebApplicationBuilder builder, Action<DesktopHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDesktopServer(builder.Configuration.GetSection(DesktopHostOptions.SectionName), configure);
        UseAppBaseWebRootWhenMissing(builder.Environment);
        return builder;
    }

    /// <summary>
    /// Replaces the configured <see cref="IServer"/> with <see cref="DesktopServer"/> on a generic-host web application.
    /// </summary>
    public static IWebHostBuilder UseDesktopServer(this IWebHostBuilder builder, Action<DesktopHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ConfigureServices((context, services) =>
            services.AddDesktopServer(context.Configuration.GetSection(DesktopHostOptions.SectionName), configure));
    }

    private static void AddDesktopServer(this IServiceCollection services, IConfiguration section, Action<DesktopHostOptions>? configure)
    {
        services.AddOptions<DesktopHostOptions>().Bind(section);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<DesktopHostSettings>();
        services.TryAddSingleton<DesktopServer>();
        services.RemoveAll<IServer>();
        services.AddSingleton<IServer>(sp => sp.GetRequiredService<DesktopServer>());
        services.TryAddSingleton<IDesktopRequestHandler>(sp => sp.GetRequiredService<DesktopServer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<MatcherPolicy, DesktopSchemeMatcherPolicy>());
    }

    // Desktop apps are often started with a working directory other than the install folder, which leaves the
    // default content-root-relative wwwroot empty. Fall back to the wwwroot shipped with the executable: next to it,
    // or in Contents/Resources of a macOS app bundle (whose assemblies live in Contents/MonoBundle).
    private static void UseAppBaseWebRootWhenMissing(IWebHostEnvironment environment)
    {
        if (!string.IsNullOrEmpty(environment.WebRootPath) && Directory.Exists(environment.WebRootPath))
        {
            return;
        }

        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "wwwroot")),
        ];

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                environment.WebRootPath = candidate;
                environment.WebRootFileProvider = new PhysicalFileProvider(candidate);
                return;
            }
        }
    }
}
