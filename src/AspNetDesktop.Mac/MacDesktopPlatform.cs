using AppKit;
using AspNetDesktop.Hosting;
using CoreGraphics;
using Foundation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
using WebKit;

namespace AspNetDesktop.Mac;

/// <summary>
/// macOS adapter: an <c>NSWindow</c> hosting <c>WKWebView</c>. <c>app://</c> and <c>ipc://</c> are served by a
/// <c>WKURLSchemeHandler</c> that forwards method, headers and body to the in-process server.
/// </summary>
/// <remarks>AppKit requires <see cref="Run"/> to be called on the process main thread.</remarks>
public sealed partial class MacDesktopPlatform : IDesktopPlatform
{
    private static int s_applicationInitialized;

    /// <inheritdoc />
    public string Name => "macos";

    /// <inheritdoc />
    public void Run(DesktopPlatformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!NSThread.IsMain)
        {
            throw new InvalidOperationException("AppKit must run on the main thread. Call RunDesktop() from Main.");
        }

        if (Interlocked.Exchange(ref s_applicationInitialized, 1) == 0)
        {
            NSApplication.Init();
        }

        DesktopHostSettings settings = context.Settings;
        ILogger logger = context.LoggerFactory.CreateLogger("AspNetDesktop.Mac");
        using CancellationTokenSource closing = new();

        NSApplication application = NSApplication.SharedApplication;
        application.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        application.MainMenu = CreateMainMenu(settings.Title);

        WKWebViewConfiguration configuration = new();
        using SchemeHandler schemeHandler = new(context.RequestHandler, logger, closing.Token);
        configuration.SetUrlSchemeHandler(schemeHandler, DesktopSchemes.App);
        configuration.SetUrlSchemeHandler(schemeHandler, DesktopSchemes.Ipc);
        configuration.UserContentController.AddUserScript(new WKUserScript(
            new NSString(context.GetBootstrapScript(this)),
            WKUserScriptInjectionTime.AtDocumentStart,
            isForMainFrameOnly: true));
        if (settings.DevToolsEnabled)
        {
            configuration.Preferences.SetValueForKey(NSNumber.FromBoolean(true), new NSString("developerExtrasEnabled"));
        }

        using NSWindow window = new(
            new CGRect(0, 0, settings.Options.Width, settings.Options.Height),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered,
            deferCreation: false)
        {
            Title = settings.Title,
        };
        window.ReleaseWhenClosed(false);

        using WKWebView webView = new(window.ContentView!.Bounds, configuration)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };

        if (settings.DevToolsEnabled && OperatingSystem.IsMacOSVersionAtLeast(13, 3))
        {
            webView.Inspectable = true;
        }

        using NavigationDelegate navigation = new(settings, logger);
        webView.NavigationDelegate = navigation;
        webView.UIDelegate = navigation;

        using WindowDelegate windowDelegate = new(application, closing);
        window.Delegate = windowDelegate;
        using AppDelegate appDelegate = new(window);
        application.Delegate = appDelegate;

        window.ContentView = webView;
        window.Center();
        if (settings.Options.Maximized)
        {
            window.Zoom(null);
        }

        using CancellationTokenRegistration stopping = context.ApplicationStopping.Register(() =>
            application.BeginInvokeOnMainThread(window.Close));

        webView.LoadRequest(new NSUrlRequest(new NSUrl(settings.StartUrl.AbsoluteUri)));
        window.MakeKeyAndOrderFront(null);
        if (OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            application.Activate();
        }
        else
        {
            application.ActivateIgnoringOtherApps(true);
        }

        // Returns when WindowDelegate stops the loop; terminate: is intercepted so the host can shut down cleanly.
        application.Run();
    }

    private static NSMenu CreateMainMenu(string title)
    {
        NSMenu appMenu = new();
        appMenu.AddItem(new NSMenuItem($"Hide {title}", new Selector("hide:"), "h"));
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        appMenu.AddItem(new NSMenuItem($"Quit {title}", new Selector("terminate:"), "q"));

        // WKWebView only receives Cmd+C/V/X/A/Z when the responder chain finds these items.
        NSMenu editMenu = new("Edit");
        editMenu.AddItem(new NSMenuItem("Undo", new Selector("undo:"), "z"));
        editMenu.AddItem(new NSMenuItem("Redo", new Selector("redo:"), "Z"));
        editMenu.AddItem(NSMenuItem.SeparatorItem);
        editMenu.AddItem(new NSMenuItem("Cut", new Selector("cut:"), "x"));
        editMenu.AddItem(new NSMenuItem("Copy", new Selector("copy:"), "c"));
        editMenu.AddItem(new NSMenuItem("Paste", new Selector("paste:"), "v"));
        editMenu.AddItem(new NSMenuItem("Select All", new Selector("selectAll:"), "a"));

        NSMenu mainMenu = new();
        mainMenu.AddItem(new NSMenuItem { Submenu = appMenu });
        mainMenu.AddItem(new NSMenuItem("Edit") { Submenu = editMenu });
        return mainMenu;
    }

    private static void OpenExternal(Uri uri, ILogger logger)
    {
        using NSUrl url = new(uri.AbsoluteUri);
        if (!NSWorkspace.SharedWorkspace.OpenUrl(url))
        {
            LogOpenExternalFailed(logger, uri);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView request {Uri} failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string? uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Uri}.")]
    private static partial void LogNavigationBlocked(ILogger logger, Uri uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Uri} in the default browser.")]
    private static partial void LogOpenExternalFailed(ILogger logger, Uri uri);

    private sealed class AppDelegate(NSWindow window) : NSApplicationDelegate
    {
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender)
        {
            // Cmd+Q: close the window instead of exit()-ing, so ASP.NET Core can stop gracefully.
            window.Close();
            return NSApplicationTerminateReply.Cancel;
        }
    }

    private sealed class WindowDelegate(NSApplication application, CancellationTokenSource closing) : NSWindowDelegate
    {
        public override void WillClose(NSNotification notification)
        {
            closing.Cancel();
            application.Stop(this);

            // Stop only takes effect after the next event; post one so Run() returns immediately.
            NSEvent wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, CGPoint.Empty, 0, 0, 0, null, 0, 0, 0)!;
            application.PostEvent(wake, atStart: true);
        }
    }

    private sealed class NavigationDelegate(DesktopHostSettings settings, ILogger logger) : WKNavigationDelegate, IWKUIDelegate
    {
        public override void DecidePolicy(WKWebView webView, WKNavigationAction navigationAction, Action<WKNavigationActionPolicy> decisionHandler)
        {
            // Only police top-level navigations; iframes keep their normal behavior.
            if (navigationAction.TargetFrame is { MainFrame: false })
            {
                decisionHandler(WKNavigationActionPolicy.Allow);
                return;
            }

            if (!Uri.TryCreate(navigationAction.Request.Url?.AbsoluteString, UriKind.Absolute, out Uri? uri))
            {
                decisionHandler(WKNavigationActionPolicy.Cancel);
                return;
            }

            DesktopNavigationDecision decision = settings.EvaluateNavigation(uri);
            if (decision == DesktopNavigationDecision.Allow)
            {
                decisionHandler(WKNavigationActionPolicy.Allow);
                return;
            }

            decisionHandler(WKNavigationActionPolicy.Cancel);
            HandleOffOrigin(uri, decision);
        }

        [Export("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:")]
        public WKWebView? CreateWebView(WKWebView webView, WKWebViewConfiguration configuration, WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
        {
            // Single-window host: UI links open in place, web links in the system browser.
            if (Uri.TryCreate(navigationAction.Request.Url?.AbsoluteString, UriKind.Absolute, out Uri? uri))
            {
                DesktopNavigationDecision decision = settings.EvaluateNavigation(uri);
                if (decision == DesktopNavigationDecision.Allow)
                {
                    webView.LoadRequest(navigationAction.Request);
                }
                else
                {
                    HandleOffOrigin(uri, decision);
                }
            }

            return null;
        }

        private void HandleOffOrigin(Uri uri, DesktopNavigationDecision decision)
        {
            if (decision == DesktopNavigationDecision.OpenExternal)
            {
                OpenExternal(uri, logger);
            }
            else
            {
                LogNavigationBlocked(logger, uri);
            }
        }
    }

    private sealed class SchemeHandler(IDesktopRequestHandler requestHandler, ILogger logger, CancellationToken closing)
        : NSObject, IWKUrlSchemeHandler
    {
        // Tasks WebKit has not stopped yet; touching a stopped task throws an Objective-C exception.
        private readonly HashSet<IntPtr> _active = [];

        [Export("webView:startURLSchemeTask:")]
        public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
        {
            IntPtr handle = urlSchemeTask.Handle;
            _active.Add(handle);
            _ = ProcessAsync(urlSchemeTask, handle);
        }

        [Export("webView:stopURLSchemeTask:")]
        public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) => _active.Remove(urlSchemeTask.Handle);

        private async Task ProcessAsync(IWKUrlSchemeTask task, IntPtr handle)
        {
            NSUrlRequest nativeRequest = task.Request;
            DesktopHttpResponse response;
            try
            {
                DesktopHttpRequest request = ToDesktopRequest(nativeRequest);
                response = await Task.Run(() => requestHandler.ProcessAsync(request, closing), closing).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRequestFailed(logger, ex, nativeRequest.Url?.AbsoluteString);
                response = DesktopHttpResponse.FromStatus(StatusCodes.Status500InternalServerError);
            }

            BeginInvokeOnMainThread(() =>
            {
                if (!_active.Remove(handle) || closing.IsCancellationRequested)
                {
                    return;
                }

                using NSHttpUrlResponse nativeResponse = new(
                    nativeRequest.Url!,
                    response.StatusCode,
                    "HTTP/1.1",
                    ToHeaderDictionary(response.Headers));
                task.DidReceiveResponse(nativeResponse);
                if (!response.Body.IsEmpty)
                {
                    using NSData data = NSData.FromArray(response.Body.ToArray());
                    task.DidReceiveData(data);
                }

                task.DidFinish();
            });
        }

        private static DesktopHttpRequest ToDesktopRequest(NSUrlRequest request)
        {
            HeaderDictionary headers = new();
            if (request.Headers is { } nativeHeaders)
            {
                foreach (KeyValuePair<NSObject, NSObject> header in nativeHeaders)
                {
                    headers.Append(header.Key.ToString(), header.Value.ToString());
                }
            }

            return new DesktopHttpRequest
            {
                Method = request.HttpMethod ?? "GET",
                Uri = new Uri(request.Url!.AbsoluteString!),
                Headers = headers,
                Body = ReadBody(request),
            };
        }

        private static MemoryStream? ReadBody(NSUrlRequest request)
        {
            if (request.Body is { Length: > 0 } data)
            {
                return new MemoryStream(data.ToArray());
            }

            // fetch() bodies can arrive as a stream instead of HTTPBody.
            if (request.BodyStream is not { } stream)
            {
                return null;
            }

            MemoryStream body = new();
            byte[] buffer = new byte[16 * 1024];
            stream.Open();
            try
            {
                while (true)
                {
                    nint read = stream.Read(buffer, (nuint)buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    body.Write(buffer, 0, (int)read);
                }
            }
            finally
            {
                stream.Close();
            }

            body.Position = 0;
            return body;
        }

        private static NSDictionary ToHeaderDictionary(IHeaderDictionary headers)
        {
            NSMutableDictionary dictionary = new();
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in headers)
            {
                dictionary[new NSString(header.Key)] = new NSString(header.Value.ToString());
            }

            return dictionary;
        }
    }
}
