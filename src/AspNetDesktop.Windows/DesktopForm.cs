using System.Diagnostics;
using System.Text;
using AspNetDesktop.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AspNetDesktop.Windows;

internal sealed partial class DesktopForm : Form
{
    private readonly DesktopPlatformContext _context;
    private readonly DesktopHostSettings _settings;
    private readonly string _bootstrapScript;
    private readonly ILogger _logger;
    private readonly WebView2 _webView;
    private readonly CancellationTokenSource _closing = new();
    private CoreWebView2Environment? _environment;
    private bool _closeRequested;

    public DesktopForm(DesktopPlatformContext context, string bootstrapScript)
    {
        _context = context;
        _settings = context.Settings;
        _bootstrapScript = bootstrapScript;
        _logger = context.LoggerFactory.CreateLogger("AspNetDesktop.Windows");

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(_settings.Options.Width, _settings.Options.Height);
        StartPosition = FormStartPosition.CenterScreen;
        Text = _settings.Title;
        if (_settings.Options.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        TrySetApplicationIcon();

        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);
    }

    /// <summary>Closes the window from any thread (used when the host stops).</summary>
    public void CloseFromAnyThread()
    {
        _closeRequested = true;
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(Close);
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_closeRequested)
        {
            Close();
            return;
        }

        try
        {
            await InitializeWebViewAsync();
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            LogWebViewFailed(_logger, ex);
            MessageBox.Show(
                this,
                "This application requires the Microsoft Edge WebView2 Runtime.\n\nInstall it from https://developer.microsoft.com/microsoft-edge/webview2/ and start the application again.",
                _settings.Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        catch (Exception ex)
        {
            LogWebViewFailed(_logger, ex);
            MessageBox.Show(this, $"The WebView could not be started.\n\n{ex.Message}", _settings.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing.Cancel();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _webView.Dispose();
            _closing.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task InitializeWebViewAsync()
    {
        // Custom schemes must be registered before the environment exists; this is what makes
        // fetch('ipc://localhost/...') work at all (and what Photino is missing).
        // (CustomSchemeRegistrations is only initialized through this constructor.)
        CoreWebView2EnvironmentOptions options = new(
            additionalBrowserArguments: null,
            language: null,
            targetCompatibleBrowserVersion: null,
            allowSingleSignOnUsingOSPrimaryAccount: false,
            customSchemeRegistrations:
            [
                CreateScheme(DesktopSchemes.App, [DesktopSchemes.AppOrigin]),
                CreateScheme(DesktopSchemes.Ipc, [.. _settings.AllowedOrigins]),
            ]);

        Directory.CreateDirectory(_settings.UserDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, _settings.UserDataFolder, options);
        await _webView.EnsureCoreWebView2Async(_environment);

        CoreWebView2 core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = _settings.DevToolsEnabled;
        core.Settings.IsStatusBarEnabled = false;

        core.AddWebResourceRequestedFilter($"{DesktopSchemes.App}:*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.AddWebResourceRequestedFilter($"{DesktopSchemes.Ipc}:*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(_bootstrapScript);
        core.Navigate(_settings.StartUrl.AbsoluteUri);
    }

    private static CoreWebView2CustomSchemeRegistration CreateScheme(string name, List<string> allowedOrigins) =>
        new(name)
        {
            TreatAsSecure = true,
            HasAuthorityComponent = true,
            AllowedOrigins = allowedOrigins,
        };

    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        CoreWebView2Deferral deferral = e.GetDeferral();
        try
        {
            // Read everything off the COM request on the UI thread, then let ASP.NET Core run on the thread pool.
            DesktopHttpRequest request = ToDesktopRequest(e.Request);
            CancellationToken closing = _closing.Token;
            DesktopHttpResponse response = await Task.Run(() => _context.RequestHandler.ProcessAsync(request, closing), closing);

            if (_environment is not null && !closing.IsCancellationRequested)
            {
                e.Response = _environment.CreateWebResourceResponse(
                    response.OpenBody(),
                    response.StatusCode,
                    response.ReasonPhrase,
                    FormatHeaders(response.Headers));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogRequestFailed(_logger, ex, e.Request.Method, e.Request.Uri);
            if (_environment is not null)
            {
                e.Response = _environment.CreateWebResourceResponse(null, StatusCodes.Status500InternalServerError, "Internal Server Error", string.Empty);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static DesktopHttpRequest ToDesktopRequest(CoreWebView2WebResourceRequest source)
    {
        HeaderDictionary headers = new();
        foreach (KeyValuePair<string, string> header in source.Headers)
        {
            headers.Append(header.Key, header.Value);
        }

        MemoryStream? body = null;
        Stream? content = source.Content;
        if (content is not null)
        {
            body = new MemoryStream();
            content.CopyTo(body);
            body.Position = 0;
        }

        return new DesktopHttpRequest
        {
            Method = source.Method,
            Uri = new Uri(source.Uri),
            Headers = headers,
            Body = body,
        };
    }

    private static string FormatHeaders(IHeaderDictionary headers)
    {
        StringBuilder builder = new();
        foreach (KeyValuePair<string, StringValues> header in headers)
        {
            foreach (string? value in header.Value)
            {
                builder.Append(header.Key).Append(": ").Append(value).Append("\r\n");
            }
        }

        return builder.ToString();
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri))
        {
            e.Cancel = true;
            return;
        }

        DesktopNavigationDecision decision = _settings.EvaluateNavigation(uri);
        if (decision != DesktopNavigationDecision.Allow)
        {
            e.Cancel = true;
            HandleOffOriginNavigation(uri, decision);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Single-window host: UI links open in place, web links in the system browser.
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        DesktopNavigationDecision decision = _settings.EvaluateNavigation(uri);
        if (decision == DesktopNavigationDecision.Allow)
        {
            _webView.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        else
        {
            HandleOffOriginNavigation(uri, decision);
        }
    }

    private void HandleOffOriginNavigation(Uri uri, DesktopNavigationDecision decision)
    {
        if (decision != DesktopNavigationDecision.OpenExternal)
        {
            LogNavigationBlocked(_logger, uri);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOpenExternalFailed(_logger, ex, uri);
        }
    }

    private void TrySetApplicationIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } processPath)
            {
                Icon = Icon.ExtractAssociatedIcon(processPath);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "WebView2 could not be initialized.")]
    private static partial void LogWebViewFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "WebView request {Method} {Uri} failed.")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string method, string uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked navigation to {Uri}.")]
    private static partial void LogNavigationBlocked(ILogger logger, Uri uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open {Uri} in the default browser.")]
    private static partial void LogOpenExternalFailed(ILogger logger, Exception exception, Uri uri);
}
