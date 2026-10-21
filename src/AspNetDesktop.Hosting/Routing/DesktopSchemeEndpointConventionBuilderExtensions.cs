using AspNetDesktop.Hosting;
using AspNetDesktop.Hosting.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Endpoint convention extensions for desktop schemes.
/// </summary>
public static class DesktopSchemeEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Restricts the endpoints to <paramref name="schemes"/>. Use <c>DesktopScheme.App</c> for endpoints that serve UI
    /// content (e.g. <c>MapStaticAssets()</c>), or <c>DesktopScheme.App | DesktopScheme.Ipc</c> for both.
    /// </summary>
    public static TBuilder RequireDesktopScheme<TBuilder>(this TBuilder builder, DesktopScheme schemes)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        RequireDesktopSchemeAttribute metadata = new(schemes);
        builder.Add(endpoint => endpoint.Metadata.Add(metadata));
        return builder;
    }
}
