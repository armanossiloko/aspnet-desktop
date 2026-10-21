using System.Runtime.Versioning;
using System.Text;
using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace AspNetDesktop.Linux;

/// <summary>
/// Linux adapter: a GTK 4 window hosting WebKitGTK 6. <c>app://</c> and <c>ipc://</c> are registered on the
/// WebKit context as secure, CORS-enabled schemes and served with full method/header/body mapping.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxDesktopPlatform : IDesktopPlatform
{
    private static int s_modulesInitialized;
    private static int s_schemesRegistered;

    private DesktopPlatformContext? _context;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private CancellationTokenSource? _closing;
    private WebKit.WebView? _webView;

    /// <inheritdoc />
    public string Name => "linux";

    /// <inheritdoc />
    public void Run(DesktopPlatformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _logger = context.LoggerFactory.CreateLogger("AspNetDesktop.Linux");
        _closing = new CancellationTokenSource();

        if (Interlocked.Exchange(ref s_modulesInitialized, 1) == 0)
        {
            Gtk.Module.Initialize();
            WebKit.Module.Initialize();
        }

        Gtk.Application application = Gtk.Application.New(CreateApplicationId(context.Settings.Title), Gio.ApplicationFlags.NonUnique);
        application.OnActivate += (_, _) => CreateWindow(application);

        using CancellationTokenRegistration stopping = context.ApplicationStopping.Register(() =>
            GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () =>
            {
                application.Quit();
                return false;
            }));

        try
        {
            // The GLib synchronization context brings awaits in the scheme handler back to the GTK thread.
            application.RunWithSynchronizationContext([]);
        }
        finally
        {
            _closing.Cancel();
            _closing.Dispose();
            _webView = null;
        }
    }

    private void CreateWindow(Gtk.Application application)
    {
        DesktopPlatformContext context = _context!;
        DesktopHostSettings settings = context.Settings;

        WebKit.NetworkSession session = WebKit.NetworkSession.New(
            Path.Combine(settings.UserDataFolder, "data"),
            Path.Combine(settings.UserDataFolder, "cache"));

        WebKit.WebView webView = WebKit.WebView.NewWithProperties(
            [new GObject.ConstructArgument("network-session", new GObject.Value(session))]);
        _webView = webView;

        RegisterSchemes(webView.GetContext());

        webView.GetSettings().SetEnableDeveloperExtras(settings.DevToolsEnabled);
        webView.GetUserContentManager().AddScript(WebKit.UserScript.New(
            context.GetBootstrapScript(this),
            WebKit.UserContentInjectedFrames.TopFrame,
            WebKit.UserScriptInjectionTime.Start,
            null,
            null));
        webView.OnDecidePolicy += OnDecidePolicy;

        Gtk.ApplicationWindow window = Gtk.ApplicationWindow.New(application);
        window.SetTitle(settings.Title);
        window.SetDefaultSize(settings.Options.Width, settings.Options.Height);
        if (settings.Options.Maximized)
        {
            window.Maximize();
        }

        window.SetChild(webView);
        window.OnCloseRequest += (_, _) =>
        {
            _closing?.Cancel();
            return false;
        };

        webView.LoadUri(settings.StartUrl.AbsoluteUri);
        window.Present();
    }

    private void RegisterSchemes(WebKit.WebContext webContext)
    {
        // Schemes belong to the process-wide WebKit context and can only be registered once.
        if (Interlocked.Exchange(ref s_schemesRegistered, 1) != 0)
        {
            return;
        }

        WebKit.SecurityManager security = webContext.GetSecurityManager();
        foreach (string scheme in new[] { DesktopSchemes.App, DesktopSchemes.Ipc })
        {
            webContext.RegisterUriScheme(scheme, OnSchemeRequest);
            security.RegisterUriSchemeAsSecure(scheme);
            security.RegisterUriSchemeAsCorsEnabled(scheme);
        }
    }

    private async void OnSchemeRequest(WebKit.URISchemeRequest request)
    {
        DesktopHttpResponse response;
        try
        {
            DesktopHttpRequest desktopRequest = ToDesktopRequest(request);
            CancellationToken closing = _closing?.Token ?? CancellationToken.None;
            response = await Task.Run(() => _context!.RequestHandler.ProcessAsync(desktopRequest, closing), closing);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            LogRequestFailed(_logger, ex, request.GetUri());
            response = DesktopHttpResponse.FromStatus(StatusCodes.Status500InternalServerError);
        }

        if (_closing?.IsCancellationRequested != false)
        {
            return;
        }

        try
        {
            request.FinishWithResponse(ToSchemeResponse(response));
        }
        catch (Exception ex)
        {
            LogRequestFailed(_logger, ex, request.GetUri());
        }
    }

    private static DesktopHttpRequest ToDesktopRequest(WebKit.URISchemeRequest request)
    {
        HeaderDictionary headers = new();
        request.GetHttpHeaders()?.Foreach((name, value) => headers.Append(name, value));

        MemoryStream? body = null;
        Gio.InputStream? input = request.GetHttpBody();
        if (input is not null)
        {
            body = new MemoryStream();
            byte[] buffer = new byte[16 * 1024];
            while (true)
            {
                int read = (int)input.Read(buffer, null);
                if (read <= 0)
                {
                    break;
                }

                body.Write(buffer, 0, read);
            }

            body.Position = 0;
        }

        return new DesktopHttpRequest
        {
            Method = request.GetHttpMethod() ?? HttpMethods.Get,
            Uri = new Uri(request.GetUri()),
            Headers = headers,
            Body = body,
        };
    }

    private static WebKit.URISchemeResponse ToSchemeResponse(DesktopHttpResponse response)
    {
        byte[] data = response.Body.ToArray();
        Gio.MemoryInputStream stream = Gio.MemoryInputStream.NewFromBytes(GLib.Bytes.New(data));
        WebKit.URISchemeResponse schemeResponse = WebKit.URISchemeResponse.New(stream, data.Length);
        schemeResponse.SetStatus((uint)response.StatusCode, response.ReasonPhrase);

        Soup.MessageHeaders headers = Soup.MessageHeaders.New(Soup.MessageHeadersType.Response);
        foreach (KeyValuePair<string, StringValues> header in response.Headers)
        {
            foreach (string? value in header.Value)
            {
                if (value is not null)
                {
                    headers.Append(header.Key, value);
                }
            }
        }

        schemeResponse.SetHttpHeaders(headers);
        string? contentType = response.Headers.ContentType;
        if (!string.IsNullOrEmpty(contentType))
        {
            schemeResponse.SetContentType(contentType);
        }

        return schemeResponse;
    }

    private bool OnDecidePolicy(WebKit.WebView sender, WebKit.WebView.DecidePolicySignalArgs args)
    {
        if (args.DecisionType is not (WebKit.PolicyDecisionType.NavigationAction or WebKit.PolicyDecisionType.NewWindowAction)
            || args.Decision is not WebKit.NavigationPolicyDecision decision)
        {
            return false;
        }

        string? target = decision.GetNavigationAction().GetRequest().GetUri();
        if (!Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            decision.Ignore();
            return true;
        }

        DesktopNavigationDecision verdict = _context!.Settings.EvaluateNavigation(uri);
        if (verdict == DesktopNavigationDecision.Allow)
        {
            if (args.DecisionType == WebKit.PolicyDecisionType.NewWindowAction)
            {
                // Single-window host: open UI links in place.
                decision.Ignore();
                sender.LoadUri(uri.AbsoluteUri);
                return true;
            }

            return false;
        }

        decision.Ignore();
        if (verdict == DesktopNavigationDecision.OpenExternal)
        {
            try
            {
                Gio.Functions.AppInfoLaunchDefaultForUri(uri.AbsoluteUri, null);
            }
            catch (Exception ex)
            {
                LogOpenExternalFailed(_logger, ex, uri);
            }
        }
        else
        {
            LogNavigationBlocked(_logger, uri);
        }

        return true;
    }

    private static string? CreateApplicationId(string title)
    {
        // GApplication ids are reverse-DNS names whose segments start with a letter.
        StringBuilder segment = new();
        foreach (char ch in title)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch == '_')
            {
                segment.Append(ch);
            }
        }

        if (segment.Length == 0)
        {
            return null;
        }

        if (!char.IsAsciiLetter(segment[0]))
        {
            segment.Insert(0, "App");
        }

        return "dev.aspnetdesktop." + segment;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView request {Uri} failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Uri}.")]
    private static partial void LogNavigationBlocked(ILogger logger, Uri uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Uri} in the default browser.")]
    private static partial void LogOpenExternalFailed(ILogger logger, Exception exception, Uri uri);
}
