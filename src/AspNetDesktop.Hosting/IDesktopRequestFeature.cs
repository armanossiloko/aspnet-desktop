namespace AspNetDesktop.Hosting;

/// <summary>
/// Per-request feature describing which desktop scheme a request arrived on.
/// </summary>
public interface IDesktopRequestFeature
{
    /// <summary>The scheme the request arrived on.</summary>
    DesktopScheme Scheme { get; }
}

internal sealed class DesktopRequestFeature(DesktopScheme scheme) : IDesktopRequestFeature
{
    public DesktopScheme Scheme { get; } = scheme;
}
