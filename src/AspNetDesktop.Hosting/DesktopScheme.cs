namespace AspNetDesktop.Hosting;

/// <summary>
/// The custom URI schemes served by the desktop host.
/// </summary>
[Flags]
public enum DesktopScheme
{
    /// <summary>No scheme.</summary>
    None = 0,

    /// <summary><c>app://localhost</c>: the UI origin (static files and the SPA fallback).</summary>
    App = 1,

    /// <summary><c>ipc://localhost</c>: the API origin, reachable only through <c>DesktopHttpClient</c>.</summary>
    Ipc = 2,
}

/// <summary>
/// Well-known names and origins of the desktop schemes.
/// </summary>
public static class DesktopSchemes
{
    /// <summary>The UI scheme name.</summary>
    public const string App = "app";

    /// <summary>The API scheme name.</summary>
    public const string Ipc = "ipc";

    /// <summary>The authority used by both schemes.</summary>
    public const string Host = "localhost";

    /// <summary>The UI origin, <c>app://localhost</c>.</summary>
    public const string AppOrigin = "app://localhost";

    /// <summary>The API origin, <c>ipc://localhost</c>.</summary>
    public const string IpcOrigin = "ipc://localhost";

    /// <summary>Header carrying the optional per-process request token.</summary>
    public const string RequestTokenHeader = "X-AspNetDesktop-Token";

    /// <summary>The base URI of the UI origin.</summary>
    public static Uri AppBaseUri { get; } = new(AppOrigin + "/");

    /// <summary>The base URI of the API origin.</summary>
    public static Uri IpcBaseUri { get; } = new(IpcOrigin + "/");

    /// <summary>
    /// Maps a URI scheme name to <see cref="DesktopScheme"/>; returns <see cref="DesktopScheme.None"/> for anything else.
    /// </summary>
    public static DesktopScheme Parse(string? scheme)
    {
        if (string.Equals(scheme, App, StringComparison.OrdinalIgnoreCase))
        {
            return DesktopScheme.App;
        }

        if (string.Equals(scheme, Ipc, StringComparison.OrdinalIgnoreCase))
        {
            return DesktopScheme.Ipc;
        }

        return DesktopScheme.None;
    }

    /// <summary>
    /// Returns the serialized origin (<c>scheme://host[:port]</c>, lower-case, no trailing slash) of an absolute URI.
    /// </summary>
    public static string GetOrigin(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("An absolute URI is required.", nameof(uri));
        }

        return (uri.Scheme + "://" + uri.Authority).ToLowerInvariant();
    }

    /// <summary>
    /// Normalizes an origin string such as <c>http://localhost:4200/</c> to <c>http://localhost:4200</c>.
    /// Returns <see langword="null"/> when the value is not an absolute URI.
    /// </summary>
    public static string? NormalizeOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        return GetOrigin(uri);
    }
}
