namespace AspNetDesktop.Hosting;

/// <summary>
/// The contract every platform adapter talks to: hand over a WebView request, get back the response.
/// </summary>
public interface IDesktopRequestHandler
{
    /// <summary>
    /// Runs <paramref name="request"/> through the desktop security policy and the ASP.NET Core pipeline.
    /// </summary>
    Task<DesktopHttpResponse> ProcessAsync(DesktopHttpRequest request, CancellationToken cancellationToken = default);
}
