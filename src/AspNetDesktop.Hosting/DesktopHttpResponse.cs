using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace AspNetDesktop.Hosting;

/// <summary>
/// A fully buffered response produced by the ASP.NET Core pipeline, ready for a platform adapter to hand back to the WebView.
/// </summary>
public sealed class DesktopHttpResponse
{
    private readonly ArraySegment<byte> _body;

    /// <summary>Creates a response.</summary>
    public DesktopHttpResponse(int statusCode, IHeaderDictionary headers, ArraySegment<byte> body, string? reasonPhrase = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        StatusCode = statusCode;
        Headers = headers;
        _body = body;
        ReasonPhrase = string.IsNullOrEmpty(reasonPhrase) ? ReasonPhrases.GetReasonPhrase(statusCode) : reasonPhrase;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The reason phrase, defaulting to the standard phrase for <see cref="StatusCode"/>.</summary>
    public string ReasonPhrase { get; }

    /// <summary>The response headers.</summary>
    public IHeaderDictionary Headers { get; }

    /// <summary>The response body.</summary>
    public ReadOnlyMemory<byte> Body => _body;

    /// <summary>Opens a new read-only stream over <see cref="Body"/>.</summary>
    public Stream OpenBody() => new MemoryStream(_body.Array ?? [], _body.Offset, _body.Count, writable: false);

    /// <summary>Creates a body-less response with only a status code.</summary>
    public static DesktopHttpResponse FromStatus(int statusCode, IHeaderDictionary? headers = null)
    {
        headers ??= new HeaderDictionary();
        headers.ContentLength = 0;
        return new DesktopHttpResponse(statusCode, headers, ArraySegment<byte>.Empty);
    }
}
