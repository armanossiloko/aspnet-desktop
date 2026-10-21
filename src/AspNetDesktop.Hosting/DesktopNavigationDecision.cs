namespace AspNetDesktop.Hosting;

/// <summary>
/// What a platform adapter should do with a top-level navigation or a new-window request.
/// </summary>
public enum DesktopNavigationDecision
{
    /// <summary>Navigate inside the WebView (UI origin or allow-listed dev server).</summary>
    Allow,

    /// <summary>Cancel the navigation and open the URI in the system browser or default handler.</summary>
    OpenExternal,

    /// <summary>Cancel the navigation.</summary>
    Block,
}
