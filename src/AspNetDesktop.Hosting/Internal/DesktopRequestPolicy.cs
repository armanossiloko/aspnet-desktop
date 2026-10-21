using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AspNetDesktop.Hosting.Internal;

/// <summary>
/// The isolation rules applied before a request reaches the ASP.NET Core pipeline:
/// scheme/host validation, the <c>ipc://</c> origin allow-list, CORS for allow-listed origins and the optional request token.
/// </summary>
internal sealed partial class DesktopRequestPolicy(DesktopHostSettings settings, ILogger logger)
{
    private const string AllowedMethods = "GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS";

    // Headers a cross-origin script can read without Access-Control-Expose-Headers.
    private static readonly HashSet<string> s_safelistedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        HeaderNames.CacheControl,
        HeaderNames.ContentLanguage,
        HeaderNames.ContentLength,
        HeaderNames.ContentType,
        HeaderNames.Expires,
        HeaderNames.LastModified,
        HeaderNames.Pragma,
    };

    private readonly byte[]? _token = settings.RequestToken is null ? null : Encoding.UTF8.GetBytes(settings.RequestToken);

    /// <summary>
    /// Classifies <paramref name="request"/> and returns a short-circuit response when it must not reach the pipeline.
    /// </summary>
    public DesktopHttpResponse? Evaluate(DesktopHttpRequest request, out DesktopScheme scheme)
    {
        scheme = DesktopSchemes.Parse(request.Uri.Scheme);
        if (scheme == DesktopScheme.None || !string.Equals(request.Uri.Host, DesktopSchemes.Host, StringComparison.OrdinalIgnoreCase))
        {
            LogRejected(logger, request.Method, request.Uri, "unknown scheme or host");
            return DesktopHttpResponse.FromStatus(StatusCodes.Status404NotFound);
        }

        if (scheme == DesktopScheme.App)
        {
            // The UI origin only serves files. A relative POST from the UI must not reach the API.
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                HeaderDictionary headers = new() { [HeaderNames.Allow] = "GET, HEAD" };
                return DesktopHttpResponse.FromStatus(StatusCodes.Status405MethodNotAllowed, headers);
            }

            return null;
        }

        string? origin = request.Headers.Origin;
        if (string.IsNullOrEmpty(origin))
        {
            if (!settings.AllowRequestsWithoutOrigin)
            {
                LogRejected(logger, request.Method, request.Uri, "missing Origin header");
                return DesktopHttpResponse.FromStatus(StatusCodes.Status403Forbidden);
            }
        }
        else if (!settings.IsOriginAllowed(origin))
        {
            LogRejected(logger, request.Method, request.Uri, $"origin '{origin}' is not allowed");
            return DesktopHttpResponse.FromStatus(StatusCodes.Status403Forbidden);
        }

        if (!string.IsNullOrEmpty(origin) && IsPreflight(request))
        {
            return CreatePreflightResponse(request, origin);
        }

        if (_token is not null && !HasValidToken(request.Headers))
        {
            LogRejected(logger, request.Method, request.Uri, "missing or invalid request token");
            return DesktopHttpResponse.FromStatus(StatusCodes.Status403Forbidden);
        }

        return null;
    }

    /// <summary>
    /// Adds the CORS response headers for an allow-listed caller of <c>ipc://</c>, including on policy rejections
    /// (e.g. a missing token) so the page sees the real status instead of an opaque network error.
    /// </summary>
    public void ApplyCors(DesktopHttpRequest request, DesktopScheme scheme, IHeaderDictionary responseHeaders)
    {
        string? origin = request.Headers.Origin;
        if (scheme != DesktopScheme.Ipc
            || string.IsNullOrEmpty(origin)
            || !settings.IsOriginAllowed(origin)
            || responseHeaders.ContainsKey(HeaderNames.AccessControlAllowOrigin))
        {
            return;
        }

        List<string> exposed = [];
        foreach (string name in responseHeaders.Keys)
        {
            if (!s_safelistedResponseHeaders.Contains(name) && !name.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase))
            {
                exposed.Add(name);
            }
        }

        responseHeaders.AccessControlAllowOrigin = origin;
        responseHeaders.AccessControlAllowCredentials = "true";
        if (exposed.Count > 0)
        {
            responseHeaders.AccessControlExposeHeaders = string.Join(", ", exposed);
        }

        responseHeaders.Vary = StringValues.Concat(responseHeaders.Vary, HeaderNames.Origin);
    }

    private static bool IsPreflight(DesktopHttpRequest request) =>
        HttpMethods.IsOptions(request.Method) && !StringValues.IsNullOrEmpty(request.Headers.AccessControlRequestMethod);

    private static DesktopHttpResponse CreatePreflightResponse(DesktopHttpRequest request, string origin)
    {
        HeaderDictionary headers = new()
        {
            [HeaderNames.AccessControlAllowOrigin] = origin,
            [HeaderNames.AccessControlAllowCredentials] = "true",
            [HeaderNames.AccessControlAllowMethods] = AllowedMethods,
            [HeaderNames.AccessControlMaxAge] = "600",
            [HeaderNames.Vary] = "Origin, Access-Control-Request-Method, Access-Control-Request-Headers",
        };

        StringValues requestedHeaders = request.Headers.AccessControlRequestHeaders;
        if (!StringValues.IsNullOrEmpty(requestedHeaders))
        {
            headers[HeaderNames.AccessControlAllowHeaders] = requestedHeaders;
        }

        return DesktopHttpResponse.FromStatus(StatusCodes.Status204NoContent, headers);
    }

    private bool HasValidToken(IHeaderDictionary headers)
    {
        string? token = headers[DesktopSchemes.RequestTokenHeader];
        return token is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _token);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected desktop request {Method} {Uri}: {Reason}.")]
    private static partial void LogRejected(ILogger logger, string method, Uri uri, string reason);
}
