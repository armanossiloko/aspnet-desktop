using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace AspNetDesktop.Hosting.Internal;

/// <summary>
/// Translates <see cref="HttpRequestMessage"/> into <see cref="DesktopHttpRequest"/> and back, without any network I/O.
/// </summary>
internal sealed class DesktopClientHandler(DesktopServer server, DesktopScheme defaultScheme) : HttpMessageHandler
{
    public static Uri GetBaseAddress(DesktopScheme scheme) => scheme switch
    {
        DesktopScheme.App => DesktopSchemes.AppBaseUri,
        DesktopScheme.Ipc => DesktopSchemes.IpcBaseUri,
        _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Choose either DesktopScheme.App or DesktopScheme.Ipc."),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);

        Uri uri = request.RequestUri;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        {
            uri = new Uri(GetBaseAddress(defaultScheme), uri.PathAndQuery);
        }

        HeaderDictionary headers = new();
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers.Append(header.Key, new StringValues([.. header.Value]));
        }

        MemoryStream? body = null;
        if (request.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                headers.Append(header.Key, new StringValues([.. header.Value]));
            }

            body = new MemoryStream();
            await request.Content.CopyToAsync(body, cancellationToken).ConfigureAwait(false);
            body.Position = 0;
        }

        string? token = server.Settings.RequestToken;
        if (token is not null && !headers.ContainsKey(DesktopSchemes.RequestTokenHeader))
        {
            headers[DesktopSchemes.RequestTokenHeader] = token;
        }

        DesktopHttpRequest desktopRequest = new()
        {
            Method = request.Method.Method,
            Uri = uri,
            Headers = headers,
            Body = body,
        };

        DesktopHttpResponse response;
        await using (body)
        {
            response = await server.ProcessAsync(desktopRequest, cancellationToken).ConfigureAwait(false);
        }

        HttpResponseMessage message = new((HttpStatusCode)response.StatusCode)
        {
            ReasonPhrase = response.ReasonPhrase,
            RequestMessage = request,
            Version = HttpVersion.Version11,
            Content = new ReadOnlyMemoryContent(response.Body),
        };

        foreach (KeyValuePair<string, StringValues> header in response.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value))
            {
                message.Content.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value);
            }
        }

        return message;
    }
}
