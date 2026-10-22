using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace AspNetDesktop.Hosting.Internal;

internal interface IHttpApplicationInvoker
{
    Task<DesktopHttpResponse> InvokeAsync(DesktopHttpRequest request, DesktopScheme scheme, CancellationToken cancellationToken);
}

/// <summary>
/// Builds the per-request <see cref="IFeatureCollection"/> and runs it through <see cref="IHttpApplication{TContext}"/>,
/// the same way Kestrel or TestServer do, but without a connection.
/// </summary>
internal sealed partial class HttpApplicationInvoker<TContext>(IHttpApplication<TContext> application, ILogger logger)
    : IHttpApplicationInvoker
    where TContext : notnull
{
    public async Task<DesktopHttpResponse> InvokeAsync(DesktopHttpRequest request, DesktopScheme scheme, CancellationToken cancellationToken)
    {
        using CancellationTokenSource aborted = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        DesktopResponseFeature response = new();
        FeatureCollection features = CreateFeatures(request, scheme, response, aborted);

        TContext context = application.CreateContext(features);
        Exception? error = null;
        try
        {
            await application.ProcessRequestAsync(context);
            await response.CompleteAsync();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        await response.FireOnCompletedAsync(ex => LogOnCompletedFailed(logger, ex));
        application.DisposeContext(context, error);

        if (error is not null)
        {
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The desktop request was aborted.", error, cancellationToken);
            }

            LogUnhandled(logger, error, request.Method, request.Uri);

            // Nothing has reached the WebView yet, so a partially written response can still become a clean 500.
            return DesktopHttpResponse.FromStatus(StatusCodes.Status500InternalServerError);
        }

        IHeaderDictionary headers = response.Headers;
        ArraySegment<byte> body = response.GetBody();
        if (headers is HeaderDictionary frozen)
        {
            // The response is complete; the policy layer may still append CORS headers.
            frozen.IsReadOnly = false;
        }

        headers.Remove(HeaderNames.TransferEncoding);
        if (HttpMethods.IsHead(request.Method))
        {
            body = ArraySegment<byte>.Empty;
        }
        else
        {
            headers.ContentLength = body.Count;
        }

        return new DesktopHttpResponse(response.StatusCode, headers, body, response.ReasonPhrase);
    }

    private static FeatureCollection CreateFeatures(
        DesktopHttpRequest request,
        DesktopScheme scheme,
        DesktopResponseFeature response,
        CancellationTokenSource aborted)
    {
        Uri uri = request.Uri;
        IHeaderDictionary headers = new HeaderDictionary();
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in request.Headers)
        {
            headers[header.Key] = header.Value;
        }

        headers.Host = uri.Authority;

        Stream body = request.Body ?? Stream.Null;
        if (request.Body is { CanSeek: true } && headers.ContentLength is null && !headers.ContainsKey(HeaderNames.TransferEncoding))
        {
            long length = request.Body.Length - request.Body.Position;
            if (length > 0)
            {
                headers.ContentLength = length;
            }
        }

        bool canHaveBody = (headers.ContentLength ?? 0) > 0 || headers.ContainsKey(HeaderNames.TransferEncoding);

        HttpRequestFeature requestFeature = new()
        {
            Protocol = "HTTP/1.1",
            Method = HttpMethods.GetCanonicalizedValue(request.Method),
            Scheme = uri.Scheme.ToLowerInvariant(),
            PathBase = string.Empty,
            Path = PathString.FromUriComponent(uri).Value ?? "/",
            QueryString = QueryString.FromUriComponent(uri).Value ?? string.Empty,
            RawTarget = uri.PathAndQuery,
            Headers = headers,
            Body = body,
        };

        FeatureCollection features = new();
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(response);
        features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature(canHaveBody));
        features.Set<IHttpRequestLifetimeFeature>(new RequestLifetimeFeature(aborted));
        features.Set<IHttpRequestIdentifierFeature>(new HttpRequestIdentifierFeature());
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature { ConnectionId = "desktop" });
        features.Set<IDesktopRequestFeature>(new DesktopRequestFeature(scheme));
        return features;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing desktop request {Method} {Uri}.")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, Uri uri);

    [LoggerMessage(Level = LogLevel.Error, Message = "An OnCompleted callback failed.")]
    private static partial void LogOnCompletedFailed(ILogger logger, Exception exception);

    private sealed class RequestBodyDetectionFeature(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody { get; } = canHaveBody;
    }

    private sealed class RequestLifetimeFeature(CancellationTokenSource aborted) : IHttpRequestLifetimeFeature
    {
        // Settable like Kestrel's: middleware such as UseRequestTimeouts() swaps in a linked token.
        public CancellationToken RequestAborted { get; set; } = aborted.Token;

        public void Abort() => aborted.Cancel();
    }
}
