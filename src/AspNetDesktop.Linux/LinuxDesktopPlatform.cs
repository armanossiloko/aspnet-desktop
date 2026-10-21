using System.Runtime.InteropServices;
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
        HeaderDictionary headers = ReadHeaders(request.GetHttpHeaders());

        MemoryStream? body = ReadBody(request);

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

        // GLib.Bytes rejects empty buffers (it reports their size as GC memory pressure).
        Gio.MemoryInputStream stream = data.Length == 0
            ? Gio.MemoryInputStream.New()
            : Gio.MemoryInputStream.NewFromBytes(GLib.Bytes.New(data));
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

    private static unsafe HeaderDictionary ReadHeaders(Soup.MessageHeaders? source)
    {
        HeaderDictionary headers = new();
        if (source is null)
        {
            return headers;
        }

        // GirCore's MessageHeaders.Foreach cannot marshal its callback, so call libsoup directly.
        GCHandle target = GCHandle.Alloc(headers);
        try
        {
            Native.MessageHeadersForeach(source.Handle.DangerousGetHandle(), &CollectHeader, GCHandle.ToIntPtr(target));
        }
        finally
        {
            target.Free();
        }

        return headers;
    }

    private static unsafe MemoryStream? ReadBody(WebKit.URISchemeRequest request)
    {
        // GirCore's GetHttpBody throws instead of returning null for body-less requests, so call WebKit directly.
        IntPtr stream = Native.UriSchemeRequestGetHttpBody(request.Handle.DangerousGetHandle());
        if (stream == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            MemoryStream body = new();
            byte[] buffer = new byte[16 * 1024];
            fixed (byte* bufferPointer = buffer)
            {
                while (true)
                {
                    IntPtr error = IntPtr.Zero;
                    nint read = Native.InputStreamRead(stream, bufferPointer, (nuint)buffer.Length, IntPtr.Zero, &error);
                    if (read < 0)
                    {
                        Native.ErrorFree(error);
                        throw new IOException("Reading the WebKit request body failed.");
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    body.Write(buffer, 0, (int)read);
                }
            }

            body.Position = 0;
            return body;
        }
        finally
        {
            Native.ObjectUnref(stream);
        }
    }

    [UnmanagedCallersOnly]
    private static void CollectHeader(IntPtr name, IntPtr value, IntPtr userData)
    {
        if (GCHandle.FromIntPtr(userData).Target is HeaderDictionary headers && Marshal.PtrToStringUTF8(name) is { } headerName)
        {
            headers.Append(headerName, Marshal.PtrToStringUTF8(value) ?? string.Empty);
        }
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

    /// <summary>Direct calls for the few APIs GirCore 0.8 binds incorrectly.</summary>
    private static unsafe partial class Native
    {
        [LibraryImport("libsoup-3.0.so.0", EntryPoint = "soup_message_headers_foreach")]
        public static partial void MessageHeadersForeach(
            IntPtr headers,
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> callback,
            IntPtr userData);

        /// <remarks>Returns a new reference (transfer full), or null when the request has no body.</remarks>
        [LibraryImport("libwebkitgtk-6.0.so.4", EntryPoint = "webkit_uri_scheme_request_get_http_body")]
        public static partial IntPtr UriSchemeRequestGetHttpBody(IntPtr request);

        [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_input_stream_read")]
        public static partial nint InputStreamRead(IntPtr stream, byte* buffer, nuint count, IntPtr cancellable, IntPtr* error);

        [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
        public static partial void ObjectUnref(IntPtr instance);

        [LibraryImport("libglib-2.0.so.0", EntryPoint = "g_error_free")]
        public static partial void ErrorFree(IntPtr error);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView request {Uri} failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Uri}.")]
    private static partial void LogNavigationBlocked(ILogger logger, Uri uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Uri} in the default browser.")]
    private static partial void LogOpenExternalFailed(ILogger logger, Exception exception, Uri uri);
}
