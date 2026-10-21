using Microsoft.AspNetCore.Http;

namespace AspNetDesktop.Hosting;

/// <summary>
/// A request captured by a platform adapter from the WebView (or by <see cref="DesktopServer.CreateClient"/>).
/// </summary>
public sealed class DesktopHttpRequest
{
    /// <summary>The HTTP method, e.g. <c>GET</c>.</summary>
    public required string Method { get; init; }

    /// <summary>The absolute request URI, e.g. <c>ipc://localhost/api/orders?page=2</c>.</summary>
    public required Uri Uri { get; init; }

    /// <summary>The request headers.</summary>
    public IHeaderDictionary Headers { get; init; } = new HeaderDictionary();

    /// <summary>The request body, or <see langword="null"/> when there is none.</summary>
    public Stream? Body { get; init; }
}
