using AspNetDesktop.Hosting.Internal;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace AspNetDesktop.Hosting;

/// <summary>
/// An <see cref="IServer"/> that never binds a socket. Requests arrive from the platform WebView adapters
/// (or <see cref="CreateClient"/>) through <see cref="ProcessAsync"/> and run through the regular ASP.NET Core pipeline.
/// </summary>
public sealed partial class DesktopServer : IServer, IDesktopRequestHandler
{
    private readonly DesktopHostSettings _settings;
    private readonly ILogger _logger;
    private readonly DesktopRequestPolicy _policy;
    private readonly ServerAddressesFeature _addresses = new();
    private IHttpApplicationInvoker? _application;
    private volatile bool _stopping;

    /// <summary>Creates the server.</summary>
    public DesktopServer(DesktopHostSettings settings, ILogger<DesktopServer> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
        _policy = new DesktopRequestPolicy(settings, logger);
        Features.Set<IServerAddressesFeature>(_addresses);
    }

    /// <inheritdoc />
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <summary>The effective desktop configuration.</summary>
    public DesktopHostSettings Settings => _settings;

    /// <summary>Whether the host has started the server and requests can be processed.</summary>
    public bool IsStarted => _application is not null && !_stopping;

    /// <inheritdoc />
    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(application);
        if (_application is not null)
        {
            throw new InvalidOperationException("The desktop server has already been started.");
        }

        // Hosting may have copied configured URLs (ASPNETCORE_URLS, launchSettings) in here; none of them are bound.
        _addresses.Addresses.Clear();
        _addresses.Addresses.Add(DesktopSchemes.AppBaseUri.ToString());
        _addresses.Addresses.Add(DesktopSchemes.IpcBaseUri.ToString());

        _application = new HttpApplicationInvoker<TContext>(application, _logger);
        _stopping = false;
        LogStarted(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<DesktopHttpResponse> ProcessAsync(DesktopHttpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Uri.IsAbsoluteUri)
        {
            throw new ArgumentException("The request URI must be absolute.", nameof(request));
        }

        IHttpApplicationInvoker? application = _application;
        if (application is null || _stopping)
        {
            return DesktopHttpResponse.FromStatus(StatusCodes.Status503ServiceUnavailable);
        }

        DesktopHttpResponse response = _policy.Evaluate(request, out DesktopScheme scheme)
            ?? await application.InvokeAsync(request, scheme, cancellationToken).ConfigureAwait(false);
        _policy.ApplyCors(request, scheme, response.Headers);
        return response;
    }

    /// <summary>
    /// Creates an <see cref="HttpMessageHandler"/> that sends requests straight into this server.
    /// Requests for <c>http</c>/<c>https</c> URIs are mapped onto <paramref name="scheme"/>.
    /// The request token, when enabled, is added automatically.
    /// </summary>
    public HttpMessageHandler CreateHandler(DesktopScheme scheme = DesktopScheme.Ipc) => new DesktopClientHandler(this, scheme);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> wired to this server, for tests and in-process callers.
    /// This is the .NET sibling of the JavaScript <c>DesktopHttpClient</c>; nothing goes over the network.
    /// </summary>
    /// <param name="scheme">The desktop scheme requests are sent to. Defaults to <c>ipc://</c>.</param>
    public HttpClient CreateClient(DesktopScheme scheme = DesktopScheme.Ipc) =>
        new(CreateHandler(scheme))
        {
            BaseAddress = DesktopClientHandler.GetBaseAddress(scheme),
        };

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping = true;
        _application = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Desktop server started on app://localhost/ and ipc://localhost/ (no network listener).")]
    private static partial void LogStarted(ILogger logger);
}
