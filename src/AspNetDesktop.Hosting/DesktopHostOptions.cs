namespace AspNetDesktop.Hosting;

/// <summary>
/// Options for the desktop host. Bound from the <c>Desktop</c> configuration section.
/// </summary>
public sealed class DesktopHostOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Desktop";

    /// <summary>The window title. Defaults to the application name.</summary>
    public string? Title { get; set; }

    /// <summary>The initial window width in device-independent pixels.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>The initial window height in device-independent pixels.</summary>
    public int Height { get; set; } = 800;

    /// <summary>Starts the window maximized.</summary>
    public bool Maximized { get; set; }

    /// <summary>
    /// The path loaded on <c>app://localhost</c> at startup. Defaults to <c>/</c>.
    /// </summary>
    public string StartPath { get; set; } = "/";

    /// <summary>
    /// A dev server (<c>ng serve</c>, Vite, ...) loaded instead of <c>app://localhost</c> when the environment is
    /// <c>Development</c>, e.g. <c>http://localhost:4200</c>. Its origin may call <c>ipc://</c> in Development only.
    /// </summary>
    public string? DevServerUrl { get; set; }

    /// <summary>
    /// Additional origins allowed to call <c>ipc://localhost</c>. <c>app://localhost</c> is always allowed.
    /// Wildcards are not supported.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>
    /// Whether <c>ipc://</c> requests without an <c>Origin</c> header (in-process callers, <c>&lt;img src&gt;</c>-style
    /// subresource loads) are accepted. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AllowRequestsWithoutOrigin { get; set; } = true;

    /// <summary>
    /// Requires every <c>ipc://</c> request to carry a random per-process token in the
    /// <c>X-AspNetDesktop-Token</c> header. The token is injected into UI pages as
    /// <c>window.__ASPNETDESKTOP__.token</c> and sent automatically by <c>DesktopHttpClient</c>.
    /// Defense in depth in case the WebView ever renders untrusted HTML.
    /// </summary>
    public bool RequireRequestToken { get; set; }

    /// <summary>
    /// Enables the WebView developer tools. Defaults to enabled in <c>Development</c> only.
    /// </summary>
    public bool? EnableDevTools { get; set; }

    /// <summary>
    /// Folder for the WebView profile (cache, cookies, local storage).
    /// Defaults to <c>&lt;LocalApplicationData&gt;/&lt;ApplicationName&gt;/WebView</c>.
    /// </summary>
    public string? UserDataFolder { get; set; }
}
